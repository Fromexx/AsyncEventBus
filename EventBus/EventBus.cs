using System.Collections.Concurrent;

namespace EventBus;

public abstract class EventBus
{
    protected enum DisposeState
    {
        Disposed = 1,
        NotDisposed = 0
    }
    
    protected readonly Dictionary<Type, List<Delegate>> HandlersByEvent = new();
    protected ConcurrentQueue<EventPublishReport> Reports { get; } = new();
    protected readonly ReaderWriterLockSlim CacheLock = new(LockRecursionPolicy.NoRecursion);
    
    protected int CurrentDisposeState = (int)DisposeState.NotDisposed;

    public IReadOnlyCollection<EventPublishReport> GetEventsReports() => Reports.ToArray();
    
    public EventPublishReport? GetEventReport<T>() where T : IEvent
    {
        foreach (var report in Reports)
        {
            if (report.EventType == typeof(T)) return report;
        }
        return null;
    }

    public IReadOnlyCollection<EventPublishReport> GetAllReports() => Reports.ToList();
    
    public void Clear<T>() where T : IEvent
    {
        ThrowIfDisposed();
        
        CacheLock.EnterWriteLock();
        try
        {
            HandlersByEvent.Remove(typeof(T));
        }
        finally
        {
            CacheLock.ExitWriteLock();
        }
    }

    public void ClearAll()
    {
        ThrowIfDisposed();
        
        CacheLock.EnterWriteLock();
        try
        {
            HandlersByEvent.Clear();
        }
        finally
        {
            CacheLock.ExitWriteLock();
        }
    }

    protected void ThrowIfDisposed()
    {
        if (Volatile.Read(ref CurrentDisposeState) != (int)DisposeState.Disposed) return;
        throw new ObjectDisposedException(nameof(AsyncEventBus));
    }
    
    private void Unsubscribe(Type eventType, Delegate handler)
    {
        CacheLock.EnterWriteLock();
        try
        {
            if (!HandlersByEvent.TryGetValue(eventType, out var handlers)) return;

            handlers.Remove(handler);
            if (handlers.Count == 0) HandlersByEvent.Remove(eventType);
        }
        finally
        {
            CacheLock.ExitWriteLock();
        }
    }
    
    protected sealed class SubscriptionToken(EventBus eventBus, Type eventType, Delegate @delegate) : IDisposable
    {
        private int _disposeState = (int)DisposeState.NotDisposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, (int)DisposeState.Disposed) == (int)DisposeState.NotDisposed)
                eventBus.Unsubscribe(eventType, @delegate);
        }
    }
}