using System.Collections.Concurrent;

namespace EventBusAsync;

public class SyncEventBus(SyncEventBusConfig config) : EventBus, IDisposable
{
    private readonly SyncEventBusConfig _config = config;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref CurrentDisposeState, (int)DisposeState.Disposed) != (int)DisposeState.NotDisposed)
            return;

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
    
    public IDisposable Subscribe<T>(Action<T> handler, bool subscribeOnce = false)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(handler);
        
        var eventType = typeof(T);
        IDisposable? subscriptionToken = null;
        
        Action<IEvent> wrapper = @event =>
        {
            if(subscribeOnce) subscriptionToken?.Dispose();
            handler.Invoke((T)@event);
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

    public void Publish<T>(T eventData) where T : IEvent
    {
        ThrowIfDisposed();
        
        List<Action<IEvent>> handlersCopy = [];
        var eventType = eventData.GetType();
        EventPublishReport report = default;
        
        CacheLock.EnterReadLock();
        try
        {
            if (!HandlersByEvent.TryGetValue(eventType, out var handlers))
                return;

            handlersCopy.AddRange(handlers.Cast<Action<IEvent>>());
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

        foreach (var handler in handlersCopy)
        {
            try
            {
                handler.Invoke(eventData);
            }
            catch (Exception exception)
            {
                if (_config.EnableReporting)
                {
                    report.Exceptions.Add(exception);
                    report.ErrorHandlers++;
                }
            }
        }

        if (!_config.EnableReporting) return;
        
        Reports.Enqueue(report);
        while (Reports.Count > _config.MaxReportHistory)
        {
            Reports.TryDequeue(out _);
        }
    }
    
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref CurrentDisposeState) != (int)DisposeState.Disposed) return;
        throw new ObjectDisposedException(nameof(AsyncEventBus));
    }
}