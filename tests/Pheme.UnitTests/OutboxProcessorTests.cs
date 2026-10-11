using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Pheme.TestFixtures;

namespace Pheme.UnitTests
{
    public class OutboxProcessorTests
    {
        private const string Recording = "Pheme.UnitTests.RecordingIntegrationEventHandler<Pheme.UnitTests.OrderPlacedIntegrationEvent>";
        private const string SecondRecording = "Pheme.UnitTests.SecondRecordingIntegrationEventHandler<Pheme.UnitTests.OrderPlacedIntegrationEvent>";

        private readonly TimerCountingTimeProvider time = new();
        private readonly TestLoggerFactory logs = new();

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public void Outbox_without_a_store_fails_on_start()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddPheme(options => options.AddOutbox())
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IStartupValidator>().Validate());

            Assert.Equal(
                "Pheme handler registrations have 1 problem:" + Environment.NewLine
                + "- The outbox is added (PhemeOptions.AddOutbox), but no outbox store is registered, so the outbox "
                + "processor would deliver nothing. Add the store of each DbContext that writes integration events with "
                + "UseOutbox<TModule>() in AddPhemeDbContext<TContext> (Pheme.EntityFrameworkCore).",
                exception.Message);
        }

        [Fact]
        public void Runner_validates_the_registrations_when_no_host_has()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(logs)
                .AddPheme(options => options.AddOutbox())
                .BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IOutboxRunner>());

            Assert.Contains("no outbox store is registered", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Delivered_message_is_marked_as_processed()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());
            OutboxMessage message = Outbox(provider).Add(provider, Placed());

            OutboxRunResult result = await Run(provider);

            Assert.Equal(1, result.Processed);
            Assert.Empty(result.Failed);
            Assert.False(result.HasMore);
            Assert.Equal(time.GetUtcNow(), Outbox(provider)[message.Id].ProcessedOnUtc);
            Assert.Equal(["RecordingIntegrationEventHandler`1"], Handlers(provider));
            Assert.Empty(logs.Entries);
            Assert.Equal(0, (await Run(provider)).Processed);
        }

        [Fact]
        public async Task Failed_handler_is_the_only_one_called_again()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, FailingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, SecondRecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());
            OutboxMessage message = Outbox(provider).Add(provider, Placed());

            OutboxRunResult first = await Run(provider);

            OutboxMessage failed = Assert.Single(first.Failed);
            Assert.Equal(0, first.Processed);
            Assert.Equal(message.Id, failed.Id);
            Assert.Equal(1, failed.AttemptCount);
            Assert.Equal($"{Recording}\n{SecondRecording}", failed.CompletedHandlers);
            Assert.StartsWith("System.InvalidOperationException: The handler failed.", failed.Error, StringComparison.Ordinal);
            Assert.Equal(time.GetUtcNow().AddSeconds(10), failed.NextAttemptOnUtc);
            OutboxMessage stored = Outbox(provider)[message.Id];
            Assert.Equal(
                (1, failed.CompletedHandlers, failed.Error, failed.NextAttemptOnUtc),
                (stored.AttemptCount, stored.CompletedHandlers, stored.Error, stored.NextAttemptOnUtc));
            Assert.Null(stored.ProcessedOnUtc);

            time.Advance(TimeSpan.FromSeconds(9));
            Assert.Empty((await Run(provider)).Failed);
            time.Advance(TimeSpan.FromSeconds(1));
            OutboxRunResult second = await Run(provider);

            Assert.Equal((2, time.GetUtcNow().AddSeconds(20)), (Assert.Single(second.Failed).AttemptCount, second.Failed[0].NextAttemptOnUtc));
            Assert.Equal(failed.CompletedHandlers, Outbox(provider)[message.Id].CompletedHandlers);
            Assert.Equal(["RecordingIntegrationEventHandler`1", "FailingIntegrationEventHandler`1", "SecondRecordingIntegrationEventHandler`1", "FailingIntegrationEventHandler`1"], Handlers(provider));
        }

        [Fact]
        public async Task Message_is_parked_after_MaxAttempts_with_an_error_log_and_warnings_before()
        {
            using ServiceProvider provider = Build(
                services => services.AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, FailingIntegrationEventHandler<OrderPlacedIntegrationEvent>>(),
                outbox => outbox.MaxAttempts = 2);
            OutboxMessage message = Outbox(provider).Add(provider, Placed());
            OutboxMessage next = Outbox(provider).Add(provider, Placed(minutes: 1));

            await Run(provider);
            time.Advance(TimeSpan.FromSeconds(10));
            await Run(provider);
            time.Advance(TimeSpan.FromDays(1));
            OutboxRunResult afterParking = await Run(provider);

            Assert.Empty(afterParking.Failed);
            OutboxMessage parked = Outbox(provider)[message.Id];
            Assert.Equal(2, parked.AttemptCount);
            Assert.Null(parked.ProcessedOnUtc);
            Assert.Null(parked.NextAttemptOnUtc);
            Assert.StartsWith("System.InvalidOperationException: The handler failed.", parked.Error, StringComparison.Ordinal);
            Assert.Equal(4, Handlers(provider).Count());
            Assert.Collection(
                logs.Entries,
                entry => AssertLogged(entry, LogLevel.Warning, new EventId(1, "OutboxMessageFailed"), $"Integration event tests.order-placed {message.Id} from outbox Tests failed on attempt 1 of 2 and will be retried in 00:00:10"),
                entry => AssertLogged(entry, LogLevel.Warning, new EventId(1, "OutboxMessageFailed"), $"Integration event tests.order-placed {next.Id} from outbox Tests failed on attempt 1 of 2 and will be retried in 00:00:10"),
                entry => AssertLogged(entry, LogLevel.Error, new EventId(2, "OutboxMessageParked"), $"Integration event tests.order-placed {message.Id} from outbox Tests failed 2 times and is parked"),
                entry => AssertLogged(entry, LogLevel.Error, new EventId(2, "OutboxMessageParked"), $"Integration event tests.order-placed {next.Id} from outbox Tests failed 2 times and is parked"));

            static void AssertLogged(LogEntry entry, LogLevel level, EventId eventId, string message)
            {
                Assert.Equal(OutboxOptions.CategoryName, entry.Category);
                Assert.Equal(level, entry.Level);
                Assert.Equal(eventId, entry.EventId);
                Assert.Equal(message, entry.Message);
                Assert.Equal("The handler failed.", Assert.IsType<InvalidOperationException>(entry.Exception).Message);
            }
        }

        [Fact]
        public async Task Message_of_an_unknown_type_fails()
        {
            using ServiceProvider provider = Build(_ => { });
            var message = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "tests.unknown",
                Content = "{}",
                Source = "Tests",
                OccurredOnUtc = DateTimeOffset.UnixEpoch,
            };
            Outbox(provider).Add(message);

            OutboxRunResult result = await Run(provider);

            Assert.StartsWith(
                "System.InvalidOperationException: No integration event handled by this application is named 'tests.unknown'.",
                Assert.Single(result.Failed).Error,
                StringComparison.Ordinal);
            Assert.Equal(1, Outbox(provider)[message.Id].AttemptCount);
        }

        [Fact]
        public async Task Messages_of_every_store_are_delivered_and_marked_in_their_store()
        {
            using ServiceProvider provider = Build(services => services
                .AddInMemoryOutboxStore("Billing")
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());
            OutboxMessage tests = Outbox(provider).Add(provider, Placed());
            OutboxMessage billing = Outbox(provider).Add(provider, Placed(), "Billing");

            OutboxRunResult result = await Run(provider);

            Assert.Equal(2, result.Processed);
            Assert.NotNull(Outbox(provider)[tests.Id].ProcessedOnUtc);
            Assert.NotNull(Outbox(provider)[billing.Id].ProcessedOnUtc);
        }

        [Fact]
        public async Task Full_batch_reports_more_messages()
        {
            using ServiceProvider provider = Build(
                services => services.AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>(),
                outbox => outbox.BatchSize = 2);
            Outbox(provider).Add(provider, Placed(minutes: 2));
            Outbox(provider).Add(provider, Placed(minutes: 1));
            Outbox(provider).Add(provider, Placed(minutes: 3));

            OutboxRunResult first = await Run(provider);
            OutboxRunResult second = await Run(provider);

            Assert.Equal((2, true), (first.Processed, first.HasMore));
            Assert.Equal((1, false), (second.Processed, second.HasMore));
            Assert.Equal([1, 2, 3], Log(provider).Deliveries.Select(delivery => delivery.Event.OccurredOn.Minute));
        }

        [Fact]
        public async Task Status_of_a_failed_message_is_saved_without_the_work_of_its_handler()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, FailingWorkIntegrationEventHandler<OrderPlacedIntegrationEvent>>());
            OutboxMessage message = Outbox(provider).Add(provider, Placed());

            OutboxRunResult result = await Run(provider);

            Assert.Single(result.Failed);
            Assert.Equal(1, Outbox(provider)[message.Id].AttemptCount);
            Assert.Empty(Outbox(provider).SavedWork);
        }

        [Fact]
        public async Task Delivery_failing_once_the_run_is_cancelled_is_not_recorded()
        {
            using var run = new CancellationTokenSource();
            using ServiceProvider provider = Build(services => services
                .AddSingleton(run)
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, CancellingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());
            OutboxMessage message = Outbox(provider).Add(provider, Placed());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => provider.GetRequiredService<IOutboxRunner>().RunOnce(run.Token));

            OutboxMessage stored = Outbox(provider)[message.Id];
            Assert.Equal(0, stored.AttemptCount);
            Assert.Null(stored.Error);
            Assert.Empty(logs.Entries);
        }

        [Fact]
        public async Task Processor_drains_a_backlog_without_waiting_then_waits_for_the_polling_interval()
        {
            using ServiceProvider provider = Build(
                services => services.AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>(),
                outbox => outbox.BatchSize = 2);
            for (int minute = 0; minute < 5; minute++)
            {
                Outbox(provider).Add(provider, Placed(minute));
            }

            IHostedService processor = await Start(provider);
            await WaitUntil(() => time.Timers == 1);

            Assert.Equal(5, Handlers(provider).Count());
            OutboxMessage late = Outbox(provider).Add(provider, Placed(minutes: 10));
            time.Advance(TimeSpan.FromSeconds(4));
            Assert.Equal((1, 5), (time.Timers, Handlers(provider).Count()));

            time.Advance(TimeSpan.FromSeconds(1));
            await WaitUntil(() => time.Timers == 2);

            Assert.NotNull(Outbox(provider)[late.Id].ProcessedOnUtc);
            await Stop(processor);
        }

        [Fact]
        public async Task Processor_drains_a_backlog_past_a_failing_message_which_waits_for_its_retry_delay()
        {
            using ServiceProvider provider = Build(
                services => services
                    .AddTransient<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, FailingIntegrationEventHandler<OrderCancelledIntegrationEvent>>()
                    .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>(),
                outbox => outbox.BatchSize = 1);
            OutboxMessage failing = Outbox(provider).Add(provider, new OrderCancelledIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch));
            for (int minute = 1; minute <= 3; minute++)
            {
                Outbox(provider).Add(provider, Placed(minute));
            }

            IHostedService processor = await Start(provider);
            await WaitUntil(() => time.Timers == 1);

            Assert.Equal(["FailingIntegrationEventHandler`1", "RecordingIntegrationEventHandler`1", "RecordingIntegrationEventHandler`1", "RecordingIntegrationEventHandler`1"], Handlers(provider));
            time.Advance(TimeSpan.FromSeconds(5));
            await WaitUntil(() => time.Timers == 2);

            Assert.Equal(1, Outbox(provider)[failing.Id].AttemptCount);
            time.Advance(TimeSpan.FromSeconds(5));
            await WaitUntil(() => time.Timers == 3);

            Assert.Equal(2, Outbox(provider)[failing.Id].AttemptCount);
            await Stop(processor);
        }

        [Fact]
        public async Task Processor_without_a_retry_delay_waits_for_the_polling_interval_after_a_failure_even_with_a_full_batch()
        {
            using ServiceProvider provider = Build(
                services => services.AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, FailingIntegrationEventHandler<OrderPlacedIntegrationEvent>>(),
                outbox =>
                {
                    outbox.BatchSize = 1;
                    outbox.RetryDelay = TimeSpan.Zero;
                });
            OutboxMessage message = Outbox(provider).Add(provider, Placed());

            IHostedService processor = await Start(provider);
            await WaitUntil(() => time.Timers == 1);

            Assert.Equal(1, Outbox(provider)[message.Id].AttemptCount);
            time.Advance(TimeSpan.FromSeconds(5));
            await WaitUntil(() => time.Timers == 2);

            Assert.Equal(2, Outbox(provider)[message.Id].AttemptCount);
            await Stop(processor);
        }

        [Fact]
        public async Task Processor_logs_a_failed_run_and_tries_again_after_the_polling_interval()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());
            OutboxMessage message = Outbox(provider).Add(provider, Placed());
            Outbox(provider).FailingReads = 1;

            IHostedService processor = await Start(provider);
            await WaitUntil(() => time.Timers == 1);

            LogEntry entry = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
            Assert.Equal(new EventId(3, "OutboxRunFailed"), entry.EventId);
            Assert.Equal("The outbox processor failed to run and tries again in 00:00:05", entry.Message);
            Assert.Equal("The database is down.", entry.Exception?.Message);
            time.Advance(TimeSpan.FromSeconds(5));
            await WaitUntil(() => time.Timers == 2);

            Assert.NotNull(Outbox(provider)[message.Id].ProcessedOnUtc);
            await Stop(processor);
        }

        [Fact]
        public async Task Processor_does_not_run_without_ProcessInBackground_and_the_runner_delivers()
        {
            using ServiceProvider provider = Build(
                services => services.AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>(),
                outbox => outbox.ProcessInBackground = false);
            OutboxMessage message = Outbox(provider).Add(provider, Placed());

            var processor = (BackgroundService)await Start(provider);
            await WaitUntil(() => processor.ExecuteTask?.IsCompleted == true);

            Assert.True(processor.ExecuteTask?.IsCompletedSuccessfully);
            Assert.Equal(0, time.Timers);
            Assert.Null(Outbox(provider)[message.Id].ProcessedOnUtc);
            Assert.Equal(1, (await Run(provider)).Processed);
            await Stop(processor);
        }

        [Fact]
        public async Task Stopped_processor_ends_its_work()
        {
            using ServiceProvider provider = Build(_ => { });

            var processor = (BackgroundService)await Start(provider);
            await WaitUntil(() => time.Timers == 1);
            await Stop(processor);

            Assert.True(processor.ExecuteTask?.IsCompletedSuccessfully);
        }

        [Fact]
        public void AddOutbox_registers_one_processor_and_runner()
        {
            IServiceCollection services = new ServiceCollection()
                .AddPheme(options => options.AddOutbox())
                .AddPheme(options => options.AddOutbox());

            Assert.Single(services, descriptor => descriptor.ImplementationType == typeof(Pheme.Outbox.OutboxProcessor));
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOutboxRunner));
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TimeProvider));
        }

        private ServiceProvider Build(Action<IServiceCollection> configure, Action<OutboxOptions>? outbox = null)
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(logs)
                .AddSingleton<TimeProvider>(time)
                .AddSingleton<OutboxLog>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>().AddOutbox(outbox ?? (_ => { })))
                .AddInMemoryOutboxStore();
            configure(services);
            return services.BuildServiceProvider(validateScopes: true);
        }

        private static OrderPlacedIntegrationEvent Placed(int minutes = 0)
        {
            return new OrderPlacedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddMinutes(minutes), "A-1");
        }

        private static InMemoryOutbox Outbox(ServiceProvider provider)
        {
            return provider.GetRequiredService<InMemoryOutbox>();
        }

        private static OutboxLog Log(ServiceProvider provider)
        {
            return provider.GetRequiredService<OutboxLog>();
        }

        private static IEnumerable<string> Handlers(ServiceProvider provider)
        {
            return Log(provider).Deliveries.Select(delivery => delivery.Handler);
        }

        private static Task<OutboxRunResult> Run(ServiceProvider provider)
        {
            return provider.GetRequiredService<IOutboxRunner>().RunOnce(Token);
        }

        private static async Task<IHostedService> Start(ServiceProvider provider)
        {
            IHostedService processor = Assert.Single(provider.GetServices<IHostedService>());
            await processor.StartAsync(Token);
            return processor;
        }

        private static Task Stop(IHostedService processor)
        {
            return processor.StopAsync(Token);
        }

        // The processor runs on its own; its progress is observed in real time.
        private static async Task WaitUntil(Func<bool> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!condition())
            {
                await Task.Delay(10, timeout.Token);
            }
        }
    }

    /// <summary>
    /// A fake clock that counts the timers created, so a test sees when the processor starts to wait.
    /// </summary>
    internal sealed class TimerCountingTimeProvider : FakeTimeProvider
    {
        private int timers;

        public int Timers => Volatile.Read(ref timers);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref timers);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
