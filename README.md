# About
This library contains two EventBus implementations: `AsyncEventBus`, which is thread-safe and designed for asynchronous and multi-threaded operations; and `SyncEventBus`, which is thread-safe and synchronous.
The core concept is subscribing to any event represented by a class/struct with optional input parameters and a constructor to initialize them (recommended). When an event is published, its subscribed handlers receive an instance of the class (i.e., the event) with the necessary parameters.

> [!TIP]
> **Features**<br/>
>✅ Asynchronous<br/>
>✅ Multi-threading<br/>
>✅ Thread-safe<br/>
>✅ Retry policy<br/>
>✅ Execution reporting<br/>
>✅ Configurable

# Using
## Event
An event is a class/struct that inherits from `IEvent`. You can define parameters for the handler within the class (properties are recommended), as well as a constructor to initialize them.
```
public class TestEvent : IEvent
{
    public int Value { get; init; }
    
    public TestEvent(int value)
    {
        Value = value;
    }
}
```
## Init and Dispose
```
AsyncEventBus _asyncEventBus = EventBusFactory.CreateAsync();
SyncEventBus _syncEventBus = EventBusFactory.CreateSync();
// Do Something
_asyncEventBus.DisposeAsync();
_syncEventBus.Dispose();
```
- **Init**: via factory.
- **Dispose**: EventBus has a built-in Dispose method that fully cleans up the EventBus instance. Calling any EventBus method after disposal will throw an `ObjectDisposedException`. For AsyncEventBus, the Dispose method is asynchronous: `DisposeAsync`.
## Subscribe
There’s no need to declare events explicitly in the EventBus; an event is automatically added to the bus on the first `Subscribe` call for that event. Subscription is done by specifying the event type and a handler whose parameter is the event class/struct.
You can also use the `subscribeOnce: bool` parameter. After the first publication of the event, all its subscribers with `subscribeOnce = true` will be unsubscribed automatically.
```
private async Task Handler(TestEvent @event)
{
    var value = @event.Value;
}
_eventBus.Subscribe<TestEvent>(Handler);
// OR
_eventBus.Subscribe<TestEvent>(Handler, true);
```
## Unsubscribe
Unsubscription is done via the return value of the `Subscribe` method: `IDisposable`. Its `Dispose` method unsubscribes the handler from the event.
```
IDisposable token = _eventBus.Subscribe<TestEvent>(Handler);
token.Dispose();
```
## Publish
For `AsyncEventBus`, use `PublishAsync`, which sends the event invocation to a `Channel` queue. For `SyncEventBus`, call `Publish`, which immediately starts invoking all handlers.
```
await asyncEventBus.PublishAsync(new TestEvent(10));
syncEventBus.Publish(new TestEvent(10));
```
## Clear
The bus provides two cleanup methods (apart from full cleanup via Dispose): `Clear` and `ClearAll`.
- `Clear`: removes a specific event and all its handlers.
- `ClearAll`: clears all events and all handlers.
```
_eventBus.Clear<TestEvent>();  
_eventBus.ClearAll();
```
## Config
You can specify custom parameters for certain EventBus settings.

| Parameter        | Description                                                                                                                                                                     | Type     | Defaults   | AsyncBus | SyncBus |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- | ---------- | :------: | :-----: |
| ShutdownTimeout  | Time to wait for handlers to complete when shutting down the EventBus                                                                                                           | TimeSpan | 5 seconds  |    ✅     |    ❌    |
| MaxRetryCount    | Number of retry attempts when a handler fails. Value **0** means no retries. Delay between retries in seconds: $$2^i$$, where *i* is the attempt number starting from 0         | int      | 2          |    ✅     |    ❌    |
| EnableReporting  | Enable/disable execution reporting (more details below)                                                                                                                         | bool     | true       |    ✅     |    ✅    |
| MaxReportHistory | Maximum number of stored reports                                                                                                                                                | int      | 5          |    ✅     |    ✅    |

### More About Reports
EventBus provides the `GetEventReport` method, which returns the latest report for a given event type (if specified) or all reports. Return type is `EventPublishReport`.

**Properties** of `EventPublishReport`:
- `EventType`: `Type` – the event type
- `HandlersCount`: `int` – number of subscribed handlers
- `ErrorHandlers`: `int` – number of handlers that threw an error during execution
- `Exceptions`: `List<Exception>` – thrown exceptions
```
var config = AsyncEventBusConfig.Default with
{
    ShutdownTimeout = TimeSpan.FromSeconds(30),
    MaxRetryCount = 3,
    EnableReporting = true
};
var eventBus = EventBusFactory.CreateAsync(config);
```
## Sync vs Async: When to Use Which
### SyncEventBus:
- Event processing is fast and synchronous
- Guarantees handler invocation order
- Guarantees handler completion order
- ⚠️ Subsequent code runs only after **ALL** handlers finish
### AsyncEventBus:
- High performance and non-blocking processing
- Events are processed in the background
- Guarantees handler invocation order
- Handlers run concurrently, the fastest finishes first
- ⚠️ Does NOT guarantee handler completion order
