using System.Threading.Channels;
using Polly;
using Polly.Retry;

namespace EventBus;

public interface IEvent;

public class AsyncEventBus : EventBus, IAsyncDisposable
{
    private readonly Channel<IEvent> _eventChannel = Channel.CreateUnbounded<IEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _processingTask;

    private readonly CancellationTokenSource _cts = new();
    private readonly AsyncEventBusConfig _config;
    private readonly AsyncRetryPolicy _retryPolicy;

    public AsyncEventBus(AsyncEventBusConfig config)
    {
        _config = config;
        _retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(retryCount: _config.MaxRetryCount,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        
        _processingTask = Task.Run(() => PublishAsync(_cts.Token));
    }
    
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref CurrentDisposeState, (int)DisposeState.Disposed) != (int)DisposeState.NotDisposed)
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
            Reports.Clear();
            
            CacheLock.EnterWriteLock();
            try
            {
                HandlersByEvent.Clear();
            }
            finally
            {
                CacheLock.ExitWriteLock();
                CacheLock.Dispose();
            }
        }
    }
    
    public IDisposable Subscribe<T>(Func<T, Task> handler, bool subscribeOnce = false)
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

        CacheLock.EnterWriteLock();
        try
        {
            if (!HandlersByEvent.TryGetValue(eventType, out var handlers))
            {
                handlers = [];
                HandlersByEvent[eventType] = handlers;
            }

            handlers.Add(wrapper);
            
            subscriptionToken = new SubscriptionToken(this, eventType, wrapper);
            return subscriptionToken;
        }
        finally
        {
            CacheLock.ExitWriteLock();
        }
    }

    public async ValueTask PublishAsync<T>(T eventData) where T : IEvent
    {
        ThrowIfDisposed();
        await _eventChannel.Writer.WriteAsync(eventData).ConfigureAwait(false);
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
        List<Func<IEvent, Task>> handlersCopy = [];
        var eventType = eventData.GetType();
        EventPublishReport report = default;
        
        CacheLock.EnterReadLock();
        try
        {
            if (!HandlersByEvent.TryGetValue(eventType, out var handlers))
                return;

            handlersCopy.AddRange(handlers.Cast<Func<IEvent, Task>>());
        }
        finally
        {
            CacheLock.ExitReadLock();
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
                    report.ErrorHandlers = exceptions.InnerExceptions.Count;
                    report.Exceptions.AddRange(exceptions.InnerExceptions);
                }
            }
        }
        finally
        {
            if (_config.EnableReporting)
            {
                Reports.Enqueue(report);
                while (Reports.Count > _config.MaxReportHistory)
                {
                    Reports.TryDequeue(out _);
                }
            }
        }
    }
}