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

    public class EventBusAsyncTest
    {
        public class EventBusSubscribeTests : IAsyncLifetime
        {
            private AsyncEventBus _asyncEventBus;

            public Task InitializeAsync()
            {
                _asyncEventBus = new AsyncEventBus();
                return Task.CompletedTask;
            }

            public async Task DisposeAsync()
            {
                await _asyncEventBus.DisposeAsync();
            }

            [Fact]
            public void Subscribe_WithValidHandler_ReturnsSubscriptionToken()
            {
                // Arrange
                var handler = new Mock<Func<IEvent, Task>>();

                // Act
                var token = _asyncEventBus.Subscribe<TestEvent>(handler.Object);

                // Assert
                Assert.NotNull(token);
                Assert.IsType<IDisposable>(token, exactMatch: false);
            }

            [Fact]
            public void Subscribe_WithNullHandler_ThrowsArgumentNullException()
            {
                // Arrange
                Func<IEvent, Task>? handler = null;

                // Act & Assert
                Assert.Throws<ArgumentNullException>(() => _asyncEventBus.Subscribe<TestEvent>(handler));
            }

            [Fact]
            public async Task Subscribe_AfterDispose_ThrowsException()
            {
                // Act
                await _asyncEventBus.DisposeAsync();

                // Assert
                Assert.Throws<ObjectDisposedException>(() =>
                    _asyncEventBus.Subscribe<TestEvent>(async e => await Task.CompletedTask));
            }
        }

        public class EventBusSubscribeOnceTests : IAsyncLifetime
        {
            private AsyncEventBus _asyncEventBus;

            public Task InitializeAsync()
            {
                _asyncEventBus = new AsyncEventBus();
                return Task.CompletedTask;
            }

            public async Task DisposeAsync()
            {
                await _asyncEventBus.DisposeAsync();
            }

            [Fact]
            public void SubscribeOnce_WithValidHandler_ReturnsSubscriptionToken()
            {
                // Arrange
                var handler = new Mock<Func<IEvent, Task>>();

                // Act
                var token = _asyncEventBus.Subscribe<TestEvent>(handler.Object, true);

                // Assert
                Assert.NotNull(token);
                Assert.IsType<IDisposable>(token, exactMatch: false);
            }

            [Fact]
            public void SubscribeOnce_WithNullHandler_ThrowsArgumentNullException()
            {
                // Arrange
                Func<IEvent, Task>? handler = null;

                // Act & Assert
                Assert.Throws<ArgumentNullException>(() => _asyncEventBus.Subscribe<TestEvent>(handler, true));
            }

            [Fact]
            public async Task SubscribeOnce_AfterDispose_ThrowsException()
            {
                // Act
                await _asyncEventBus.DisposeAsync();

                // Assert
                Assert.Throws<ObjectDisposedException>(() =>
                    _asyncEventBus.Subscribe<TestEvent>(async e => await Task.CompletedTask, true));
            }
        }

        public class EventBusUnsubscribeTests : IAsyncLifetime
        {
            private AsyncEventBus _asyncEventBus;

            public Task InitializeAsync()
            {
                _asyncEventBus = new AsyncEventBus();
                return Task.CompletedTask;
            }

            public async Task DisposeAsync()
            {
                await _asyncEventBus.DisposeAsync();
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
                using (_asyncEventBus.Subscribe<TestEvent>(handler))
                {
                    await _asyncEventBus.PublishAsync(testEvent);
                    await Task.Delay(50);
                }

                await _asyncEventBus.PublishAsync(testEvent);
                await Task.Delay(50);

                Assert.Equal(1, callCount);
            }

            [Fact]
            public async Task SubscribeOnce_ManualDispose_NoCalls()
            {
                // Arrange
                var eventBus = new AsyncEventBus();
                var callsCount = 0;

                // Act
                var token = eventBus.Subscribe<TestEvent>(async e =>
                {
                    Interlocked.Increment(ref callsCount);
                    await Task.CompletedTask;
                }, true);

                token.Dispose();

                await eventBus.PublishAsync(new TestEvent());
                await Task.Delay(100);

                // Assert
                Assert.Equal(0, callsCount);
            }
        }

        public class EventBusPublishTests : IAsyncLifetime
        {
            private AsyncEventBus _asyncEventBus;

            public Task InitializeAsync()
            {
                _asyncEventBus = new AsyncEventBus();
                return Task.CompletedTask;
            }

            public async Task DisposeAsync()
            {
                await _asyncEventBus.DisposeAsync();
            }

            [Fact]
            public async Task PublishAsync_WithSubscribedHandler_CallsHandler()
            {
                // Arrange
                var wasCalled = false;
                var testEvent = new TestEvent { Data = "Test" };
                Func<IEvent, Task> handler = async e =>
                {
                    wasCalled = true;
                    await Task.CompletedTask;
                };

                _asyncEventBus.Subscribe<TestEvent>(handler);

                // Act
                await _asyncEventBus.PublishAsync(testEvent);
                await Task.Delay(100);

                // Assert
                Assert.True(wasCalled);
            }

            [Fact]
            public async Task PublishAsync_WithMultipleHandlers_CallsAllHandlers()
            {
                // Arrange
                var callCount = 0;
                var testEvent = new TestEvent { Data = "Test" };

                _asyncEventBus.Subscribe<TestEvent>(async e =>
                {
                    Interlocked.Increment(ref callCount);
                    await Task.CompletedTask;
                });

                _asyncEventBus.Subscribe<TestEvent>(async e =>
                {
                    Interlocked.Increment(ref callCount);
                    await Task.CompletedTask;
                });

                // Act
                await _asyncEventBus.PublishAsync(testEvent);
                await Task.Delay(100);

                // Assert
                Assert.Equal(2, callCount);
            }

            [Fact]
            public async Task PublishAsync_WithSpecificEventType_OnlyCallsMatchingHandlers()
            {
                // Arrange
                var testEventCallCount = 0;
                var anotherEventCallCount = 0;

                _asyncEventBus.Subscribe<TestEvent>(async e =>
                {
                    Interlocked.Increment(ref testEventCallCount);
                    await Task.CompletedTask;
                });

                _asyncEventBus.Subscribe<AnotherTestEvent>(async e =>
                {
                    Interlocked.Increment(ref anotherEventCallCount);
                    await Task.CompletedTask;
                });

                // Act
                await _asyncEventBus.PublishAsync(new TestEvent { Data = "Test" });
                await Task.Delay(100);

                // Assert
                Assert.Equal(1, testEventCallCount);
                Assert.Equal(0, anotherEventCallCount);
            }

            [Fact]
            public async Task PublishAsync_WithMultipleEvents_ProcessesInOrder()
            {
                // Arrange
                var events = new ConcurrentQueue<string>();
                var testEvent1 = new TestEvent { Data = "Event1" };
                var testEvent2 = new TestEvent { Data = "Event2" };
                var testEvent3 = new TestEvent { Data = "Event3" };

                _asyncEventBus.Subscribe<TestEvent>(async e =>
                {
                    events.Enqueue(e.Data);
                    await Task.Delay(10);
                });

                // Act
                await _asyncEventBus.PublishAsync(testEvent1);
                await _asyncEventBus.PublishAsync(testEvent2);
                await _asyncEventBus.PublishAsync(testEvent3);
                await Task.Delay(150);

                // Assert
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
                // Arrange
                var callCount = 0;
                var testEvent = new TestEvent { Data = "Test" };

                _asyncEventBus.Subscribe(new Func<TestEvent, Task>(async e =>
                {
                    Interlocked.Increment(ref callCount);
                    throw new InvalidOperationException("Test exception");
                }));

                _asyncEventBus.Subscribe(new Func<TestEvent, Task>(async e =>
                {
                    Interlocked.Increment(ref callCount);
                    await Task.CompletedTask;
                }));

                // Act
                await _asyncEventBus.PublishAsync(testEvent);
                await Task.Delay(7000);

                // Assert
                Assert.Equal(4, callCount);
            }

            [Fact]
            public async Task PublishAsync_AfterDispose_ThrowsException()
            {
                // Act & Arrange
                await _asyncEventBus.DisposeAsync();
                var testEvent = new TestEvent { Data = "Test" };

                // Act & Assert
                await Assert.ThrowsAsync<ObjectDisposedException>(() => _asyncEventBus.PublishAsync(testEvent).AsTask());
            }

            [Fact]
            public async Task PublishAsync_SubscribeOnceHandler_CallsOneTime()
            {
                // Arrange
                var callsCount = 0;
                var testEvent = new TestEvent { Data = "Test" };
                Func<TestEvent, Task> handler = async e =>
                {
                    Interlocked.Increment(ref callsCount);
                    await Task.CompletedTask;
                };

                _asyncEventBus.Subscribe(handler, true);

                // Act
                await _asyncEventBus.PublishAsync(testEvent);
                await _asyncEventBus.PublishAsync(testEvent);
                await Task.Delay(100);

                // Assert
                Assert.Equal(1, callsCount);
            }
        }

        public class EventBusClearTests : IAsyncLifetime
        {
            private AsyncEventBus _asyncEventBus;

            public Task InitializeAsync()
            {
                _asyncEventBus = new AsyncEventBus();
                return Task.CompletedTask;
            }

            public async Task DisposeAsync()
            {
                await _asyncEventBus.DisposeAsync();
            }

            [Fact]
            public async Task Clear_RemovesAllHandlersForEventType()
            {
                // Arrange
                var callCount = 0;
                Func<TestEvent, Task> handler = async e =>
                {
                    Interlocked.Increment(ref callCount);
                    await Task.CompletedTask;
                };

                var token1 = _asyncEventBus.Subscribe(handler);
                var token2 = _asyncEventBus.Subscribe<TestEvent>(async e =>
                {
                    Interlocked.Increment(ref callCount);
                    await Task.CompletedTask;
                });

                // Act
                _asyncEventBus.Clear<TestEvent>();

                await _asyncEventBus.PublishAsync(new TestEvent());

                token1.Dispose();
                token2.Dispose();

                // Assert
                Assert.Equal(0, callCount);
            }

            [Fact]
            public async Task ClearAll_RemovesAllHandlers()
            {
                // Arrange
                var callCount = 0;
                Func<TestEvent, Task> handler = async e =>
                {
                    Interlocked.Increment(ref callCount);
                    await Task.CompletedTask;
                };

                var token1 = _asyncEventBus.Subscribe(handler);
                var token2 = _asyncEventBus.Subscribe<AnotherTestEvent>(async e =>
                {
                    Interlocked.Increment(ref callCount);
                    await Task.CompletedTask;
                });

                // Act
                _asyncEventBus.ClearAll();

                await _asyncEventBus.PublishAsync(new TestEvent());
                await _asyncEventBus.PublishAsync(new AnotherTestEvent());

                token1.Dispose();
                token2.Dispose();

                // Assert
                Assert.Equal(0, callCount);
            }

            [Fact]
            public async Task Clear_DisposeSubscriptionToken_NothingHappens()
            {
                // Arrange
                Func<TestEvent, Task> handler = async e =>
                {
                    await Task.CompletedTask;
                };

                var token = _asyncEventBus.Subscribe(handler);
                
                // Act
                _asyncEventBus.Clear<TestEvent>();
                token.Dispose();
            }
        }

        public class EventBusDisposeTests : IAsyncLifetime
        {
            private AsyncEventBus _asyncEventBus;

            public Task InitializeAsync()
            {
                _asyncEventBus = new AsyncEventBus();
                return Task.CompletedTask;
            }

            public async Task DisposeAsync()
            {
                await _asyncEventBus.DisposeAsync();
            }

            [Fact]
            public async Task DisposeAsync_StopsProcessing()
            {
                // Arrange
                var callCount = 0;
                var testEvent = new TestEvent { Data = "Test" };
                var handlerStarted = new TaskCompletionSource<bool>();
                var handlerCanContinue = new ManualResetEventSlim(false);

                _asyncEventBus.Subscribe<TestEvent>(async e =>
                {
                    Interlocked.Increment(ref callCount);
                    handlerStarted.TrySetResult(true);

                    handlerCanContinue.Wait(TimeSpan.FromSeconds(2));

                    await Task.Delay(100);
                });

                // Act
                await _asyncEventBus.PublishAsync(testEvent);
                await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

                await _asyncEventBus.DisposeAsync();

                handlerCanContinue.Set();

                await Task.Delay(200);

                //Assert
                Assert.Equal(1, callCount);

                await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                    _asyncEventBus.PublishAsync(new TestEvent()).AsTask());
            }

            [Fact]
            public async Task DisposeAsync_CanBeCalledMultipleTimes()
            {
                // Act
                await _asyncEventBus.DisposeAsync();
                await _asyncEventBus.DisposeAsync();
            }

            [Fact]
            public async Task DisposeAsync_WaitsForCurrentHandlers_ThenCancels()
            {
                // Arrange
                var handlerStarted = new ManualResetEventSlim(false);
                var handlerCanFinish = new ManualResetEventSlim(false);
                var handlerCompleted = false;
                var disposeCompleted = false;

                _asyncEventBus.Subscribe<TestEvent>(async e =>
                {
                    handlerStarted.Set();

                    handlerCanFinish.Wait(TimeSpan.FromSeconds(10));

                    handlerCompleted = true;
                    await Task.CompletedTask;
                });

                // Act
                await _asyncEventBus.PublishAsync(new TestEvent());
                handlerStarted.Wait(TimeSpan.FromSeconds(1));

                var disposeTask = Task.Run(async () =>
                {
                    await _asyncEventBus.DisposeAsync();
                    disposeCompleted = true;
                });
                await Task.Delay(100);

                // Assert
                Assert.False(disposeCompleted, "DisposeAsync should not complete while the handler is running.");
                handlerCanFinish.Set();

                await disposeTask.WaitAsync(TimeSpan.FromSeconds(6));

                Assert.True(handlerCompleted, "The handler should have ended");
                Assert.True(disposeCompleted, "DisposeAsync should have ended");

                await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                    _asyncEventBus.PublishAsync(new TestEvent()).AsTask());
            }
        }

        public class EventBusReportTests : IAsyncLifetime
        {
            private AsyncEventBus _asyncEventBus;

            public Task InitializeAsync()
            {
                _asyncEventBus = new AsyncEventBus();
                return Task.CompletedTask;
            }

            public async Task DisposeAsync()
            {
                await _asyncEventBus.DisposeAsync();
            }

            [Fact]
            public async Task CheckReport_WithValidHandlers()
            {
                // Arrange
                Func<TestEvent, Task> handler1 = e => Task.CompletedTask;
                Func<TestEvent, Task> handler2 = e => Task.CompletedTask;

                _asyncEventBus.Subscribe(handler1);
                _asyncEventBus.Subscribe(handler2);
                
                // Act
                await _asyncEventBus.PublishAsync(new TestEvent());
                await Task.Delay(100);

                // Assert
                var report = _asyncEventBus.GetEventReport<TestEvent>();
                
                Assert.Equal(typeof(TestEvent), report!.EventType);
                Assert.Equal(2, report.HandlersCount);
                Assert.Equal(0, report.ErrorHandlers);
                Assert.Empty(report.Exceptions);
            }
            
            [Fact]
            public async Task CheckReport_WithThrowingHandler()
            {
                // Arrange
                var callCount = 0;
                var testEvent = new TestEvent { Data = "Test" };

                _asyncEventBus.Subscribe(new Func<TestEvent, Task>(async e =>
                {
                    Interlocked.Increment(ref callCount);
                    throw new InvalidOperationException("Test exception");
                }));

                _asyncEventBus.Subscribe(new Func<TestEvent, Task>(async e =>
                {
                    Interlocked.Increment(ref callCount);
                    throw new ArgumentException("Test exception");
                }));

                // Act
                await _asyncEventBus.PublishAsync(testEvent);
                await Task.Delay(7000);

                // Assert
                var report = _asyncEventBus.GetEventReport<TestEvent>();
                
                Assert.Equal(typeof(TestEvent), report!.EventType);
                Assert.Equal(2, report.HandlersCount);
                Assert.Equal(2, report.ErrorHandlers);
                Assert.Contains(report.Exceptions, ex => ex is InvalidOperationException);
                Assert.Contains(report.Exceptions, ex => ex is ArgumentException);
            }
        }

        public class EventBusOtherTests : IAsyncLifetime
        {
            private AsyncEventBus _asyncEventBus;

            public Task InitializeAsync()
            {
                var config = EventBusConfig.Default with
                {
                    MaxReportHistory = 1,
                };
                
                _asyncEventBus = new AsyncEventBus(config);
                return Task.CompletedTask;
            }

            public async Task DisposeAsync()
            {
                await _asyncEventBus.DisposeAsync();
            }

            [Fact]
            public async Task MultipleEvents_InParallel_AreProcessed()
            {
                // Arrange
                var processedEvents = new ConcurrentBag<string>();
                var tasks = new List<Task>();
                var testEvent = new TestEvent();
                var allProcessed = new TaskCompletionSource<bool>();
                var expectedCount = 10;

                _asyncEventBus.Subscribe<TestEvent>(async e =>
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

                // Act
                for (var i = 0; i < expectedCount; i++)
                {
                    tasks.Add(_asyncEventBus.PublishAsync(testEvent).AsTask());
                }

                await Task.WhenAll(tasks);
                await allProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));

                // Assert
                Assert.Equal(expectedCount, processedEvents.Count);
            }

            [Fact]
            public async Task EventData_IsPassedCorrectly()
            {
                // Arrange
                TestEvent? receivedEvent = null;
                var originalEvent = new TestEvent { Data = "Important Data" };

                _asyncEventBus.Subscribe<TestEvent>(async e =>
                {
                    receivedEvent = e;
                    await Task.CompletedTask;
                });

                // Act
                await _asyncEventBus.PublishAsync(originalEvent);
                await Task.Delay(100);

                // Assert
                Assert.NotNull(receivedEvent);
                Assert.Equal("Important Data", receivedEvent.Data);
                Assert.Same(originalEvent, receivedEvent);
            }

            [Fact]
            public async Task Handler_WithAsyncOperation_Completes()
            {
                // Arrange
                var completionSource = new TaskCompletionSource<bool>();
                var testEvent = new TestEvent { Data = "Test" };

                _asyncEventBus.Subscribe<TestEvent>(async e =>
                {
                    await Task.Delay(50);
                    completionSource.SetResult(true);
                });

                // Act
                await _asyncEventBus.PublishAsync(testEvent);
                var result = await completionSource.Task.WaitAsync(TimeSpan.FromSeconds(1));

                // Assert
                Assert.True(result);
            }

            [Fact]
            public async Task MultipleSubscribeUnsubscribe_ThreadSafety()
            {
                // Arrange
                var exceptions = new ConcurrentBag<Exception>();
                var tasks = new List<Task>();

                // Act
                for (var i = 0; i < 10; i++)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            for (var j = 0; j < 100; j++)
                            {
                                Func<IEvent, Task> handler = async e => await Task.CompletedTask;
                                var token = _asyncEventBus.Subscribe<TestEvent>(handler);
                                token.Dispose();
                            }
                        }
                        catch (Exception exception)
                        {
                            exceptions.Add(exception);
                        }
                    }));
                }

                await Task.WhenAll(tasks);
                await _asyncEventBus.DisposeAsync();

                // Assert
                Assert.Empty(exceptions);
            }

            [Fact]
            public async Task Publish_WithChangedConfig_OnlyOneReport()
            {
                // Arrange
                Func<TestEvent, Task> handler1 = e => Task.CompletedTask;
                Func<AnotherTestEvent, Task> handler2 = e => Task.CompletedTask;

                _asyncEventBus.Subscribe(handler1);
                _asyncEventBus.Subscribe(handler2);
                
                // Act
                await _asyncEventBus.PublishAsync(new TestEvent());
                await Task.Delay(100);
                await _asyncEventBus.PublishAsync(new AnotherTestEvent());
                await Task.Delay(100);
                
                // Assert
                Assert.Null(_asyncEventBus.GetEventReport<TestEvent>());
                Assert.NotNull(_asyncEventBus.GetEventReport<AnotherTestEvent>());
            }
        }
    }
}