namespace EventBus;

public struct EventPublishReport
{
     public Type EventType { get; set; }
     public int HandlersCount { get; set; }
     public int ErrorHandlers { get; set; }
     public List<Exception> Exceptions { get; set; }
}