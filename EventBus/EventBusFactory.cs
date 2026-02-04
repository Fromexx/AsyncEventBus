namespace EventBusAsync;

public static class EventBusFactory
{
    public static AsyncEventBus CreateAsync(AsyncEventBusConfig? config = null) => new(config ?? AsyncEventBusConfig.Default);
    public static SyncEventBus CreateSync(SyncEventBusConfig? config = null) => new(config ?? SyncEventBusConfig.Default);
}