using System.Collections.Concurrent;
using System.Threading.Channels;
using Polly;
using Polly.Retry;

namespace EventBusAsync;

public interface IEvent;

public class AsyncEventBus : IAsyncDisposable
{
    private enum DisposeState
    {
        Disposed = 1,
        NotDisposed = 0
    }
    
    private readonly Dictionary<Type, List<Func<IEvent, Task>>> _handlersByEvent = new();
    private readonly ConcurrentQueue<EventPublishReport> _reports;
    
    private readonly Channel<IEvent> _eventChannel = Channel.CreateUnbounded<IEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _processingTask;

    private readonly ReaderWriterLockSlim _cacheLock = new(LockRecursionPolicy.NoRecursion);
    private readonly CancellationTokenSource _cts = new();
    private readonly EventBusConfig _config;
    private readonly AsyncRetryPolicy _retryPolicy;
    
    private int _disposeState = (int)DisposeState.NotDisposed;

    public AsyncEventBus(EventBusConfig? config = null)
    {
        _config = config ?? EventBusConfig.Default;
        _reports = new ConcurrentQueue<EventPublishReport>();
        _retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(retryCount: _config.MaxRetryCount,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        
        _processingTask = Task.Run(() => PublishAsync(_cts.Token));
    }
    
    private sealed class SubscriptionToken(AsyncEventBus asyncEventBus, Type eventType, Func<IEvent, Task> handler) : IDisposable
    {
        private int _disposeState = (int)DisposeState.NotDisposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, (int)DisposeState.Disposed) == (int)DisposeState.NotDisposed)
                asyncEventBus.Unsubscribe(eventType, handler);
        }
    }
    
    public IDisposable Subscribe<T>(Func<T, Task> handler, bool subscribeOnce = false) where T : IEvent
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(handler);
        
        var eventType = typeof(T);
        IDisposable? subscriptionToken = null;
        
        Func<IEvent, Task> wrapper = @event =>
        {
            if(subscribeOnce) subscriptionToken?.Dispose();
            return _retryPolicy.ExecuteAsync(async () => { await handler.Invoke((T)@event); });
        };

        _cacheLock.EnterWriteLock();
        try
        {
            if (!_handlersByEvent.TryGetValue(eventType, out var handlers))
            {
                handlers = [];
                _handlersByEvent[eventType] = handlers;
            }

            handlers.Add(wrapper);
            
            subscriptionToken = new SubscriptionToken(this, eventType, wrapper);
            return subscriptionToken;
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public async ValueTask PublishAsync<T>(T eventData) where T : IEvent
    {
        ThrowIfDisposed();
        await _eventChannel.Writer.WriteAsync(eventData).ConfigureAwait(false);
    }

    public EventPublishReport? GetEventReport<T>() where T : IEvent => _reports.FirstOrDefault(report => report.EventType == typeof(T));
    
    public void Clear<T>() where T : IEvent
    {
        ThrowIfDisposed();
        
        _cacheLock.EnterWriteLock();
        try
        {
            _handlersByEvent.Remove(typeof(T));
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
            _handlersByEvent.Clear();
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
            await _processingTask.WaitAsync(_config.ShutdownTimeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        finally
        {
            _cts.Dispose();
            _reports.Clear();
            
            _cacheLock.EnterWriteLock();
            try
            {
                _handlersByEvent.Clear();
            }
            finally
            {
                _cacheLock.ExitWriteLock();
                _cacheLock.Dispose();
            }
        }
    }
    
    private void Unsubscribe(Type eventType, Func<IEvent, Task> handler)
    {
        _cacheLock.EnterWriteLock();
        try
        {
            if (!_handlersByEvent.TryGetValue(eventType, out var handlers)) return;

            handlers.Remove(handler);
            if (handlers.Count == 0) _handlersByEvent.Remove(eventType);
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
    
    private async Task PublishSingleAsync<T>(T eventData) where T : IEvent
    {
        List<Func<IEvent, Task>> handlersCopy;
        var eventType = eventData.GetType();
        EventPublishReport? report = null;
        
        _cacheLock.EnterReadLock();
        try
        {
            if (!_handlersByEvent.TryGetValue(eventType, out var handlers))
                return;

            handlersCopy = [..handlers];
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }
        
        if (_config.EnableReporting)
        {
            report = new EventPublishReport
            {
                EventType = eventType,
                HandlersCount = handlersCopy.Count,
                Exceptions = []
            };
        }
        
        var tasks = new List<Task>(handlersCopy.Count);
        tasks.AddRange(handlersCopy.Select(handler => handler.Invoke(eventData)));

        var allTasks = Task.WhenAll(tasks);
        try
        {
            await allTasks;
        }
        catch (Exception)
        {
            if (_config.EnableReporting)
            {
                if (allTasks.Exception is { } exceptions)
                {
                    report!.ErrorHandlers = exceptions.InnerExceptions.Count;
                    report.Exceptions.AddRange(exceptions.InnerExceptions);
                }
            }
        }
        finally
        {
            if (_config.EnableReporting)
            {
                _reports.Enqueue(report!);
                while (_reports.Count > _config.MaxReportHistory)
                {
                    _reports.TryDequeue(out _);
                }
            }
        }
    }
    
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != (int)DisposeState.Disposed) return;
        throw new ObjectDisposedException(nameof(AsyncEventBus));
    }
}