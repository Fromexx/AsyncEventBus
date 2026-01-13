using System.Threading.Channels;

namespace EventBusAsync;

public interface IEvent;

public class EventBus : IAsyncDisposable
{
    private enum DisposeState
    {
        Disposed = 1,
        NotDisposed = 0
    }
    
    private readonly Dictionary<Type, List<Func<IEvent, Task>>> _handlers = new();
    private readonly ReaderWriterLockSlim _cacheLock = new(LockRecursionPolicy.NoRecursion);
    private readonly Channel<IEvent> _eventChannel = Channel.CreateUnbounded<IEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;
    private int _disposeState = (int)DisposeState.NotDisposed;

    public EventBus()
    {
        _processingTask = Task.Run(() => PublishAsync(_cts.Token));
    }

    public IDisposable Subscribe<T>(Func<IEvent, Task> handler) where T : IEvent
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(T);

        _cacheLock.EnterWriteLock();
        try
        {
            if (!_handlers.TryGetValue(eventType, out var handlers))
            {
                handlers = [];
                _handlers[eventType] = handlers;
            }

            handlers.Add(handler);

            return new SubscriptionToken(this, eventType, handler);
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public IDisposable SubscribeOnce<T>(Func<IEvent, Task> handler) where T : IEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        
        IDisposable? subscription = null;
    
        Task Wrapper(IEvent e)
        {
            subscription?.Dispose();
            return handler.Invoke(e);
        }
        
        subscription = Subscribe<T>(Wrapper);
        return subscription;
    }

    public bool Unsubscribe<T>(Func<IEvent, Task> handler) where T : IEvent
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(handler);
        return Unsubscribe(typeof(T), handler);
    }

    public async ValueTask PublishAsync<T>(T eventData) where T : IEvent
    {
        ThrowIfDisposed();
        await _eventChannel.Writer.WriteAsync(eventData).ConfigureAwait(false);
    }

    public void Clear<T>()
    {
        ThrowIfDisposed();
        
        _cacheLock.EnterWriteLock();
        try
        {
            _handlers.Remove(typeof(T));
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public void ClearAll()
    {
        ThrowIfDisposed();
        
        _cacheLock.EnterWriteLock();
        try
        {
            _handlers.Clear();
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, (int)DisposeState.Disposed) != (int)DisposeState.NotDisposed)
            return;

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _eventChannel.Writer.TryComplete();
            await _processingTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        finally
        {
            _cts.Dispose();
            _cacheLock.EnterWriteLock();
            try
            {
                _handlers.Clear();
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }
    }
    
    private sealed class SubscriptionToken(EventBus eventBus, Type eventType, Func<IEvent, Task> handler) : IDisposable
    {
        private int _disposeState = (int)DisposeState.NotDisposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, (int)DisposeState.Disposed) == (int)DisposeState.NotDisposed)
                eventBus.Unsubscribe(eventType, handler);
        }
    }
    
    private bool Unsubscribe(Type eventType, Func<IEvent, Task> handler)
    {
        _cacheLock.EnterWriteLock();
        try
        {
            if (!_handlers.TryGetValue(eventType, out var handlers))
                return false;

            if (!handlers.Remove(handler) || handlers.Count != 0) return false;
            _handlers.Remove(eventType);
            return true;
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }
    
    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var eventData in _eventChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await PublishSingleAsync(eventData).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested){}
    }
    
    private async Task PublishSingleAsync(IEvent eventData)
    {
        List<Func<IEvent, Task>> handlersCopy;
        var eventType = eventData.GetType();
        
        _cacheLock.EnterReadLock();
        try
        {
            if (!_handlers.TryGetValue(eventType, out var handlers))
                return;

            handlersCopy = [..handlers];
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }
        
        var tasks = new List<Task>(handlersCopy.Count);
        
        foreach (var handler in handlersCopy)
        {
            try
            {
                var task = handler.Invoke(eventData);
                tasks.Add(task);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Error in handler: {exception.Message}");
            }
        }
        
        if (tasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Error in handler: {exception.Message}");
            }
        }
    }
    
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != (int)DisposeState.Disposed) return;
        throw new ObjectDisposedException(nameof(EventBus));
    }
}