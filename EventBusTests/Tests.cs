using System.Collections.Concurrent;
using Moq;
using Xunit;
using EventBusAsync;

namespace EventBusTests
{
    public class TestEvent : IEvent
    {
        public string Data { get; init; }
    }

    public class AnotherTestEvent : IEvent
    {
        public int Value { get; set; }
    }

    public class EventBusSubscribeTests : IAsyncLifetime
    {
        private EventBus _eventBus;

        public Task InitializeAsync()
        {
            _eventBus = new EventBus();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _eventBus.DisposeAsync();
        }

        [Fact]
        public void Subscribe_WithValidHandler_ReturnsSubscriptionToken()
        {
            var handler = new Mock<Func<IEvent, Task>>();

            var token = _eventBus.Subscribe<TestEvent>(handler.Object);

            Assert.NotNull(token);
            Assert.IsType<IDisposable>(token, exactMatch: false);
        }

        [Fact]
        public void Subscribe_WithNullHandler_ThrowsArgumentNullException()
        {
            Func<IEvent, Task>? handler = null;

            Assert.Throws<ArgumentNullException>(() => _eventBus.Subscribe<TestEvent>(handler));
        }

        [Fact]
        public async Task Subscribe_AfterDispose_ThrowsException()
        {
            await _eventBus.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(() =>
                _eventBus.Subscribe<TestEvent>(async e => await Task.CompletedTask));
        }
    }
    
    public class EventBusUnsubscribeTests : IAsyncLifetime
    {
        private EventBus _eventBus;

        public Task InitializeAsync()
        {
            _eventBus = new EventBus();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _eventBus.DisposeAsync();
        }
        
        [Fact]
        public async Task Unsubscribe_RemovesHandler()
        {
            var callCount = 0;
            var testEvent = new TestEvent { Data = "Test" };
            Func<IEvent, Task> handler = async e =>
            {
                Interlocked.Increment(ref callCount);
                await Task.CompletedTask;
            };
            _eventBus.Subscribe<TestEvent>(handler);
            
            await _eventBus.PublishAsync(testEvent);
            await Task.Delay(50);
            var result = _eventBus.Unsubscribe<TestEvent>(handler);
            await _eventBus.PublishAsync(testEvent);
            await Task.Delay(50);

            Assert.True(result);
            Assert.Equal(1, callCount);
        }

        [Fact]
        public void Unsubscribe_WithNullHandler_ThrowsArgumentNullException()
        {
            Func<IEvent, Task>? handler = null;

            Assert.Throws<ArgumentNullException>(() => _eventBus.Unsubscribe<TestEvent>(handler));
        }
        
        [Fact]
        public async Task SubscriptionToken_Dispose_UnsubscribesHandler()
        {
            var callCount = 0;
            var testEvent = new TestEvent { Data = "Test" };
            Func<IEvent, Task> handler = async e =>
            {
                Interlocked.Increment(ref callCount);
                await Task.CompletedTask;
            };
            using (var token = _eventBus.Subscribe<TestEvent>(handler))
            {
                await _eventBus.PublishAsync(testEvent);
                await Task.Delay(50);
            }
            
            await _eventBus.PublishAsync(testEvent);
            await Task.Delay(50);

            Assert.Equal(1, callCount);
        }
        
        [Fact]
        public async Task Unsubscribe_NonExistentHandler_ReturnsFalse()
        {
            Func<IEvent, Task> handler = async e => await Task.CompletedTask;

            var result = _eventBus.Unsubscribe<TestEvent>(handler);

            Assert.False(result);
        }
    }

    public class EventBusPublishTests : IAsyncLifetime
    {
        private EventBus _eventBus;

        public Task InitializeAsync()
        {
            _eventBus = new EventBus();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _eventBus.DisposeAsync();
        }
        
        [Fact]
        public async Task PublishAsync_WithSubscribedHandler_CallsHandler()
        {
            var wasCalled = false;
            var testEvent = new TestEvent { Data = "Test" };
            Func<IEvent, Task> handler = async e =>
            {
                wasCalled = true;
                await Task.CompletedTask;
            };
            _eventBus.Subscribe<TestEvent>(handler);
            
            await _eventBus.PublishAsync(testEvent);
            await Task.Delay(100);

            Assert.True(wasCalled);
        }

        [Fact]
        public async Task PublishAsync_WithMultipleHandlers_CallsAllHandlers()
        {
            var callCount = 0;
            var testEvent = new TestEvent { Data = "Test" };
            _eventBus.Subscribe<TestEvent>(async e =>
            {
                Interlocked.Increment(ref callCount);
                await Task.CompletedTask;
            });
            _eventBus.Subscribe<TestEvent>(async e =>
            {
                Interlocked.Increment(ref callCount);
                await Task.CompletedTask;
            });

            await _eventBus.PublishAsync(testEvent);
            await Task.Delay(100);

            Assert.Equal(2, callCount);
        }

        [Fact]
        public async Task PublishAsync_WithSpecificEventType_OnlyCallsMatchingHandlers()
        {
            var testEventCallCount = 0;
            var anotherEventCallCount = 0;
            _eventBus.Subscribe<TestEvent>(async e =>
            {
                Interlocked.Increment(ref testEventCallCount);
                await Task.CompletedTask;
            });
            _eventBus.Subscribe<AnotherTestEvent>(async e =>
            {
                Interlocked.Increment(ref anotherEventCallCount);
                await Task.CompletedTask;
            });

            await _eventBus.PublishAsync(new TestEvent { Data = "Test" });
            await Task.Delay(100);

            Assert.Equal(1, testEventCallCount);
            Assert.Equal(0, anotherEventCallCount);
        }
        
        [Fact]
        public async Task PublishAsync_WithMultipleEvents_ProcessesInOrder()
        {
            var events = new ConcurrentQueue<string>();
            var testEvent1 = new TestEvent { Data = "Event1" };
            var testEvent2 = new TestEvent { Data = "Event2" };
            var testEvent3 = new TestEvent { Data = "Event3" };
            
            _eventBus.Subscribe<TestEvent>(async e =>
            {
                var testEvent = (TestEvent)e;
                events.Enqueue(testEvent.Data);
                await Task.Delay(10);
            });

            await _eventBus.PublishAsync(testEvent1);
            await _eventBus.PublishAsync(testEvent2);
            await _eventBus.PublishAsync(testEvent3);
            await Task.Delay(150);

            Assert.Equal(3, events.Count);
            
            Assert.True(events.TryDequeue(out var data1));
            Assert.Equal("Event1", data1);
            
            Assert.True(events.TryDequeue(out var data2));
            Assert.Equal("Event2", data2);
            
            Assert.True(events.TryDequeue(out var data3));
            Assert.Equal("Event3", data3);
        }

        [Fact]
        public async Task PublishAsync_WithThrowingHandler_DoesNotBreakEventBus()
        {
            var callCount = 0;
            var testEvent = new TestEvent { Data = "Test" };

            _eventBus.Subscribe<TestEvent>(async e =>
            {
                Interlocked.Increment(ref callCount);
                throw new InvalidOperationException("Test exception");
            });

            _eventBus.Subscribe<TestEvent>(async e =>
            {
                Interlocked.Increment(ref callCount);
                await Task.CompletedTask;
            });

            await _eventBus.PublishAsync(testEvent);
            await Task.Delay(100);
            await _eventBus.PublishAsync(testEvent);
            await Task.Delay(100);

            Assert.Equal(4, callCount);
        }
        
        [Fact]
        public async Task PublishAsync_AfterDispose_ThrowsException()
        {
            await _eventBus.DisposeAsync();
            var testEvent = new TestEvent { Data = "Test" };

            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                _eventBus.PublishAsync(testEvent).AsTask());
        }
    }

    public class EventBusClearTests : IAsyncLifetime
    {
        private EventBus _eventBus;

        public Task InitializeAsync()
        {
            _eventBus = new EventBus();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _eventBus.DisposeAsync();
        }
        
        [Fact]
        public void Clear_RemovesAllHandlersForEventType()
        {
            var callCount = 0;
            Func<IEvent, Task> handler = async e =>
            {
                Interlocked.Increment(ref callCount);
                await Task.CompletedTask;
            };
            _eventBus.Subscribe<TestEvent>(handler);
            _eventBus.Subscribe<TestEvent>(async e =>
            {
                Interlocked.Increment(ref callCount);
                await Task.CompletedTask;
            });

            _eventBus.Clear<TestEvent>();

            Assert.False(_eventBus.Unsubscribe<TestEvent>(handler));
        }

        [Fact]
        public void ClearAll_RemovesAllHandlers()
        {
            Func<IEvent, Task> handler1 = async e => await Task.CompletedTask;
            Func<IEvent, Task> handler2 = async e => await Task.CompletedTask;

            _eventBus.Subscribe<TestEvent>(handler1);
            _eventBus.Subscribe<AnotherTestEvent>(handler2);

            _eventBus.ClearAll();

            Assert.False(_eventBus.Unsubscribe<TestEvent>(handler1));
            Assert.False(_eventBus.Unsubscribe<AnotherTestEvent>(handler2));
        }
    }

    public class EventBusDisposeTests : IAsyncLifetime
    {
        private EventBus _eventBus;

        public Task InitializeAsync()
        {
            _eventBus = new EventBus();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _eventBus.DisposeAsync();
        }
        
        [Fact]
        public async Task DisposeAsync_StopsProcessing()
        {
            var callCount = 0;
            var testEvent = new TestEvent { Data = "Test" };
            var handlerStarted = new TaskCompletionSource<bool>();
            var handlerCanContinue = new ManualResetEventSlim(false);

            _eventBus.Subscribe<TestEvent>(async e =>
            {
                Interlocked.Increment(ref callCount);
                handlerStarted.TrySetResult(true);
        
                handlerCanContinue.Wait(TimeSpan.FromSeconds(2));
        
                await Task.Delay(100);
            });

            await _eventBus.PublishAsync(testEvent);
            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            
            await _eventBus.DisposeAsync();
    
            handlerCanContinue.Set();
            
            await Task.Delay(200);

            Assert.Equal(1, callCount);
    
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                _eventBus.PublishAsync(new TestEvent()).AsTask());
        }

        [Fact]
        public async Task DisposeAsync_CanBeCalledMultipleTimes()
        {
            await _eventBus.DisposeAsync();
            await _eventBus.DisposeAsync();
        }
        
        [Fact]
        public async Task DisposeAsync_WaitsForCurrentHandlers_ThenCancels()
        {
            var handlerStarted = new ManualResetEventSlim(false);
            var handlerCanFinish = new ManualResetEventSlim(false);
            var handlerCompleted = false;
            var disposeCompleted = false;

            _eventBus.Subscribe<TestEvent>(async e =>
            {
                handlerStarted.Set();
        
                handlerCanFinish.Wait(TimeSpan.FromSeconds(10));
        
                handlerCompleted = true;
                await Task.CompletedTask;
            });

            await _eventBus.PublishAsync(new TestEvent());
            handlerStarted.Wait(TimeSpan.FromSeconds(1));

            var disposeTask = Task.Run(async () =>
            {
                await _eventBus.DisposeAsync();
                disposeCompleted = true;
            });
            await Task.Delay(100);
    
            Assert.False(disposeCompleted, "DisposeAsync не должен завершиться, пока обработчик выполняется");
            handlerCanFinish.Set();
    
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(6));

            Assert.True(handlerCompleted, "Обработчик должен был завершиться");
            Assert.True(disposeCompleted, "DisposeAsync должен был завершиться");
    
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                _eventBus.PublishAsync(new TestEvent()).AsTask());
        }
    }

    public class EventBusOtherTests : IAsyncLifetime
    {
        private EventBus _eventBus;

        public Task InitializeAsync()
        {
            _eventBus = new EventBus();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _eventBus.DisposeAsync();
        }
        
        [Fact]
        public async Task MultipleEvents_InParallel_AreProcessed()
        {
            var processedEvents = new ConcurrentBag<string>();
            var tasks = new List<Task>();
            var testEvent = new TestEvent();
            var allProcessed = new TaskCompletionSource<bool>();
            var expectedCount = 10;

            _eventBus.Subscribe<TestEvent>(async e =>
            {
                lock (processedEvents)
                {
                    processedEvents.Add($"Processed at {DateTime.Now.Ticks}");
                    if (processedEvents.Count == expectedCount)
                    {
                        allProcessed.TrySetResult(true);
                    }
                }
                await Task.Delay(20);
            });

            for (var i = 0; i < expectedCount; i++)
            {
                tasks.Add(_eventBus.PublishAsync(testEvent).AsTask());
            }

            await Task.WhenAll(tasks);
            await allProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(expectedCount, processedEvents.Count);
        }

        [Fact]
        public async Task EventData_IsPassedCorrectly()
        {
            TestEvent? receivedEvent = null;
            var originalEvent = new TestEvent { Data = "Important Data" };

            _eventBus.Subscribe<TestEvent>(async e =>
            {
                receivedEvent = (TestEvent)e;
                await Task.CompletedTask;
            });

            await _eventBus.PublishAsync(originalEvent);
            await Task.Delay(100);

            Assert.NotNull(receivedEvent);
            Assert.Equal("Important Data", receivedEvent.Data);
            Assert.Same(originalEvent, receivedEvent);
        }

        [Fact]
        public async Task Handler_WithAsyncOperation_Completes()
        {
            var completionSource = new TaskCompletionSource<bool>();
            var testEvent = new TestEvent { Data = "Test" };

            _eventBus.Subscribe<TestEvent>(async e =>
            {
                await Task.Delay(50);
                completionSource.SetResult(true);
            });

            await _eventBus.PublishAsync(testEvent);
            
            var result = await completionSource.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(result);
        }
        
        [Fact]
        public async Task MultipleSubscribeUnsubscribe_ThreadSafety()
        {
            var exceptions = new ConcurrentBag<Exception>();
            var tasks = new List<Task>();

            for (var i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (var j = 0; j < 100; j++)
                        {
                            Func<IEvent, Task> handler = async e => await Task.CompletedTask;
                            var token = _eventBus.Subscribe<TestEvent>(handler);
                            
                            if (j % 3 == 0)
                            {
                                _eventBus.Unsubscribe<TestEvent>(handler);
                            }
                            else
                            {
                                token.Dispose();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }));
            }

            await Task.WhenAll(tasks);
            await _eventBus.DisposeAsync();

            Assert.Empty(exceptions);
        }
    }
}