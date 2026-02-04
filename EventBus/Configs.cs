namespace EventBusAsync;

public record struct AsyncEventBusConfig(TimeSpan ShutdownTimeout, int MaxRetryCount, bool EnableReporting, int MaxReportHistory)
{
    public static AsyncEventBusConfig Default { get; } = new(
        ShutdownTimeout: TimeSpan.FromSeconds(5),
        MaxRetryCount: 2,
        EnableReporting: true,
        MaxReportHistory: 5
    );
}

public record struct SyncEventBusConfig(bool EnableReporting, int MaxReportHistory)
{
    public static SyncEventBusConfig Default { get; } = new(
        EnableReporting: true,
        MaxReportHistory: 5
    );
}