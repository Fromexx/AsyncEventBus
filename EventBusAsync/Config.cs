namespace EventBusAsync;

public record struct EventBusConfig(TimeSpan ShutdownTimeout, int MaxRetryCount, bool EnableReporting, int MaxReportHistory)
{
    public static EventBusConfig Default { get; } = new(
        ShutdownTimeout: TimeSpan.FromSeconds(5),
        MaxRetryCount: 2,
        EnableReporting: true,
        MaxReportHistory: 5
    );
}