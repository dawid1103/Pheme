using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pheme.TestFixtures;

namespace Pheme.UnitTests
{
    public class OutboxMaintenanceTests
    {
        private const string Recording = "Pheme.UnitTests.RecordingIntegrationEventHandler<Pheme.UnitTests.OrderPlacedIntegrationEvent>";

        private readonly TimerCountingTimeProvider time = new();
        private readonly TestLoggerFactory logs = new();

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Parked_messages_of_all_stores_are_listed_oldest_first_up_to_the_limit()
        {
            using ServiceProvider provider = Build(services => services.AddInMemoryOutboxStore("Billing"));
            OutboxMessage third = Park(provider, minutes: 3);
            OutboxMessage first = Park(provider, minutes: 1, source: "Billing");
            OutboxMessage second = Park(provider, minutes: 2);
            Outbox(provider).Add(provider, Placed(minutes: 0));

            IReadOnlyList<OutboxMessage> all = await Maintain(provider, maintenance => maintenance.GetParked(cancellationToken: Token));
            IReadOnlyList<OutboxMessage> two = await Maintain(provider, maintenance => maintenance.GetParked(2, Token));

            Assert.Equal([first.Id, second.Id, third.Id], all.Select(message => message.Id));
            Assert.Equal(["Billing", "Tests", "Tests"], all.Select(message => message.Source));
            Assert.All(all, message => Assert.Equal("The handler failed.", message.Error));
            Assert.Equal([first.Id, second.Id], two.Select(message => message.Id));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                "limit", () => Maintain(provider, maintenance => maintenance.GetParked(0, Token)));
        }

        [Fact]
        public async Task Requeued_message_is_delivered_again_to_the_handlers_that_did_not_complete()
        {
            using ServiceProvider provider = Build(
                services => services
                    .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                    .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, FailingIntegrationEventHandler<OrderPlacedIntegrationEvent>>(),
                outbox => outbox.MaxAttempts = 1);
            OutboxMessage message = Outbox(provider).Add(provider, Placed());
            await Run(provider);
            Assert.Equal([message.Id], (await Maintain(provider, maintenance => maintenance.GetParked(cancellationToken: Token))).Select(parked => parked.Id));

            bool requeued = await Maintain(provider, maintenance => maintenance.Requeue(message.Id, Token));

            Assert.True(requeued);
            OutboxMessage stored = Outbox(provider)[message.Id];
            Assert.Equal((0, null, Recording), (stored.AttemptCount, stored.Error, stored.CompletedHandlers));
            Assert.Empty(await Maintain(provider, maintenance => maintenance.GetParked(cancellationToken: Token)));
            await Run(provider);
            Assert.Equal(["RecordingIntegrationEventHandler`1", "FailingIntegrationEventHandler`1", "FailingIntegrationEventHandler`1"], Log(provider).Deliveries.Select(delivery => delivery.Handler));
        }

        [Fact]
        public async Task Requeue_of_a_processed_or_unknown_message_returns_false()
        {
            using ServiceProvider provider = Build(services => services
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());
            OutboxMessage message = Outbox(provider).Add(provider, Placed());
            await Run(provider);

            Assert.False(await Maintain(provider, maintenance => maintenance.Requeue(message.Id, Token)));
            Assert.False(await Maintain(provider, maintenance => maintenance.Requeue(Guid.NewGuid(), Token)));
            Assert.NotNull(Outbox(provider)[message.Id].ProcessedOnUtc);
        }

        [Fact]
        public async Task All_parked_messages_are_requeued_across_pages_in_every_store_or_in_one()
        {
            using ServiceProvider provider = Build(services => services.AddInMemoryOutboxStore("Billing"));
            for (int minute = 0; minute < 150; minute++)
            {
                Park(provider, minute);
            }

            OutboxMessage billing = Park(provider, minutes: 0, source: "Billing");

            int requeuedTests = await Maintain(provider, maintenance => maintenance.RequeueAllParked("Tests", Token));

            Assert.Equal(150, requeuedTests);
            Assert.Equal([billing.Id], (await Maintain(provider, maintenance => maintenance.GetParked(cancellationToken: Token))).Select(message => message.Id));
            Park(provider, minutes: 1);
            Assert.Equal(2, await Maintain(provider, maintenance => maintenance.RequeueAllParked(cancellationToken: Token)));
            Assert.Empty(await Maintain(provider, maintenance => maintenance.GetParked(cancellationToken: Token)));
        }

        [Fact]
        public async Task Requeue_of_an_unknown_source_throws_with_the_store_names()
        {
            using ServiceProvider provider = Build(services => services.AddInMemoryOutboxStore("Billing"));

            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
                "source", () => Maintain(provider, maintenance => maintenance.RequeueAllParked("Shipping", Token)));

            Assert.StartsWith("No outbox store is named 'Shipping'. The stores are 'Tests', 'Billing'.", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Retention_deletes_only_processed_messages_older_than_the_threshold_on_start_and_every_hour()
        {
            using ServiceProvider provider = Build(_ => { }, outbox => outbox.ProcessedRetention = TimeSpan.FromDays(1));
            OutboxMessage old = Processed(provider, TimeSpan.FromDays(2));
            OutboxMessage recent = Processed(provider, TimeSpan.FromHours(23.5));
            OutboxMessage parked = Park(provider, minutes: 0);

            IHostedService processor = await Start(provider);
            await WaitUntil(() => time.Timers == 1);

            Assert.False(Outbox(provider).Contains(old.Id));
            Assert.True(Outbox(provider).Contains(recent.Id));
            Assert.True(Outbox(provider).Contains(parked.Id));

            time.Advance(TimeSpan.FromMinutes(59));
            await WaitUntil(() => time.Timers == 2);
            Assert.True(Outbox(provider).Contains(recent.Id));
            time.Advance(TimeSpan.FromMinutes(1));
            await WaitUntil(() => time.Timers == 3);

            Assert.False(Outbox(provider).Contains(recent.Id));
            Assert.True(Outbox(provider).Contains(parked.Id));
            await processor.StopAsync(Token);
        }

        [Fact]
        public async Task Retention_off_keeps_processed_messages()
        {
            using ServiceProvider provider = Build(_ => { }, outbox => outbox.ProcessedRetention = null);
            OutboxMessage old = Processed(provider, TimeSpan.FromDays(400));

            IHostedService processor = await Start(provider);
            await WaitUntil(() => time.Timers == 1);

            Assert.True(Outbox(provider).Contains(old.Id));
            await processor.StopAsync(Token);
        }

        [Fact]
        public async Task Failed_retention_of_a_store_is_logged_and_neither_stops_the_others_nor_the_delivery()
        {
            using ServiceProvider provider = Build(services => services
                .AddInMemoryOutboxStore("Billing")
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>());
            OutboxMessage billing = Processed(provider, TimeSpan.FromDays(60), "Billing");
            OutboxMessage pending = Outbox(provider).Add(provider, Placed());
            Outbox(provider).FailingDeletes = 1;

            IHostedService processor = await Start(provider);
            await WaitUntil(() => time.Timers == 1);

            LogEntry entry = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
            Assert.Equal(new EventId(4, "OutboxRetentionFailed"), entry.EventId);
            Assert.Equal("The outbox processor failed to delete the processed messages of outbox Tests", entry.Message);
            Assert.False(Outbox(provider).Contains(billing.Id));
            Assert.NotNull(Outbox(provider)[pending.Id].ProcessedOnUtc);
            await processor.StopAsync(Token);
        }

        [Fact]
        public async Task Retention_logs_for_each_store_what_it_deleted_and_when_the_next_deletion_is_due()
        {
            using ServiceProvider provider = Build(
                services => services.AddInMemoryOutboxStore("Billing"), outbox => outbox.ProcessedRetention = TimeSpan.FromDays(1));
            Processed(provider, TimeSpan.FromDays(2));
            Processed(provider, TimeSpan.FromDays(3));
            DateTimeOffset start = time.GetUtcNow();

            IHostedService processor = await Start(provider);
            await WaitUntil(() => time.Timers == 1);

            Assert.Collection(
                logs.Entries,
                tests => AssertRetentionLog(tests, LogLevel.Information, "Tests", 2, start),
                billing => AssertRetentionLog(billing, LogLevel.Information, "Billing", 0, start));
            await processor.StopAsync(Token);
        }

        [Fact]
        public async Task Retention_log_has_the_configured_level()
        {
            using ServiceProvider provider = Build(_ => { }, outbox => outbox.RetentionLogLevel = LogLevel.Warning);

            IHostedService processor = await Start(provider);
            await WaitUntil(() => time.Timers == 1);

            Assert.Equal(LogLevel.Warning, Assert.Single(logs.Entries).Level);
            await processor.StopAsync(Token);
        }

        [Fact]
        public async Task Retention_log_level_none_logs_nothing()
        {
            using ServiceProvider provider = Build(_ => { }, outbox => outbox.RetentionLogLevel = LogLevel.None);

            IHostedService processor = await Start(provider);
            await WaitUntil(() => time.Timers == 1);

            Assert.Empty(logs.Entries);
            await processor.StopAsync(Token);
        }

        [Fact]
        public void RetentionLogLevel_must_be_a_defined_level()
        {
            Assert.Equal(LogLevel.Information, new OutboxOptions().RetentionLogLevel);
            Assert.Throws<ArgumentOutOfRangeException>("value", () => new OutboxOptions { RetentionLogLevel = (LogLevel)42 });
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ProcessedRetention_must_be_positive(int days)
        {
            Assert.Throws<ArgumentOutOfRangeException>("value", () => new OutboxOptions { ProcessedRetention = TimeSpan.FromDays(days) });
        }

        [Fact]
        public async Task Health_check_is_healthy_degraded_by_an_old_pending_message_and_unhealthy_with_a_parked_one()
        {
            using ServiceProvider provider = Build(services => services
                .AddInMemoryOutboxStore("Billing")
                .AddHealthChecks()
                .AddPhemeOutboxCheck(maxPendingAge: TimeSpan.FromMinutes(10), tags: ["outbox"]));
            HealthCheckService health = provider.GetRequiredService<HealthCheckService>();

            HealthReportEntry empty = (await health.CheckHealthAsync(Token)).Entries["pheme_outbox"];
            Assert.Equal(HealthStatus.Healthy, empty.Status);
            Assert.Equal(["outbox"], empty.Tags);
            Assert.Equal(new Dictionary<string, object> { ["Tests.pending"] = 0, ["Tests.parked"] = 0, ["Billing.pending"] = 0, ["Billing.parked"] = 0 }, empty.Data);

            Outbox(provider).Add(provider, new OrderPlacedIntegrationEvent(Guid.NewGuid(), time.GetUtcNow().AddMinutes(-10), "A-1"));
            Assert.Equal(HealthStatus.Healthy, (await health.CheckHealthAsync(Token)).Status);

            time.Advance(TimeSpan.FromSeconds(1));
            HealthReportEntry late = (await health.CheckHealthAsync(Token)).Entries["pheme_outbox"];
            Assert.Equal(HealthStatus.Degraded, late.Status);
            Assert.Equal("Outbox: Tests has a message pending for 00:10:01, longer than 00:10:00.", late.Description);
            Assert.Equal(TimeSpan.FromSeconds(601), late.Data["Tests.oldestPendingAge"]);

            Park(provider, minutes: 0, source: "Billing");
            HealthReportEntry parked = (await health.CheckHealthAsync(Token)).Entries["pheme_outbox"];
            Assert.Equal(HealthStatus.Unhealthy, parked.Status);
            Assert.Equal("Outbox: Billing has 1 parked message; Tests has a message pending for 00:10:01.", parked.Description);
            Assert.Equal(1, parked.Data["Billing.parked"]);
        }

        [Fact]
        public async Task Health_check_reports_parked_messages_with_the_failure_status_and_its_name()
        {
            using ServiceProvider provider = Build(services => services
                .AddHealthChecks()
                .AddPhemeOutboxCheck(name: "outbox", failureStatus: HealthStatus.Degraded));
            Park(provider, minutes: 0);
            Park(provider, minutes: 1);

            HealthReportEntry entry = (await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(Token)).Entries["outbox"];

            Assert.Equal(HealthStatus.Degraded, entry.Status);
            Assert.Equal("Outbox: Tests has 2 parked messages.", entry.Description);
        }

        [Fact]
        public void Health_check_rejects_a_max_pending_age_that_is_not_positive()
        {
            IHealthChecksBuilder builder = new ServiceCollection().AddHealthChecks();

            Assert.Throws<ArgumentOutOfRangeException>("maxPendingAge", () => builder.AddPhemeOutboxCheck(TimeSpan.Zero));
        }

        private static void AssertRetentionLog(LogEntry entry, LogLevel level, string source, int deleted, DateTimeOffset start)
        {
            Assert.Equal((OutboxOptions.CategoryName, level, new EventId(5, "OutboxRetentionCompleted")), (entry.Category, entry.Level, entry.EventId));
            Assert.Equal(source, entry.Properties["Source"]);
            Assert.Equal(deleted, entry.Properties["DeletedCount"]);
            Assert.Equal(start - TimeSpan.FromDays(1), entry.Properties["ProcessedBefore"]);
            Assert.Equal(start + Pheme.Outbox.OutboxProcessor.RetentionInterval, entry.Properties["NextRun"]);
        }

        private ServiceProvider Build(Action<IServiceCollection> configure, Action<OutboxOptions>? outbox = null)
        {
            IServiceCollection services = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(logs)
                .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
                .AddSingleton<TimeProvider>(time)
                .AddSingleton<OutboxLog>()
                .AddPheme(options => options.AddHandlersFromAssemblyContaining<Ping>().AddOutbox(outbox ?? (_ => { })))
                .AddInMemoryOutboxStore()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>();
            configure(services);
            return services.BuildServiceProvider(validateScopes: true);
        }

        private static OrderPlacedIntegrationEvent Placed(int minutes = 0)
        {
            return new OrderPlacedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddMinutes(minutes), "A-1");
        }

        /// <summary>
        /// Stores a message parked after the default number of attempts.
        /// </summary>
        private static OutboxMessage Park(ServiceProvider provider, int minutes, string source = "Tests")
        {
            OutboxMessage message = provider.GetRequiredService<IOutboxMessageFactory>().Create(Placed(minutes), source);
            message.AttemptCount = new OutboxOptions().MaxAttempts;
            message.Error = "The handler failed.";
            Outbox(provider).Add(message);
            return message;
        }

        private OutboxMessage Processed(ServiceProvider provider, TimeSpan ago, string source = "Tests")
        {
            OutboxMessage message = provider.GetRequiredService<IOutboxMessageFactory>().Create(Placed(), source);
            message.ProcessedOnUtc = time.GetUtcNow() - ago;
            Outbox(provider).Add(message);
            return message;
        }

        private static InMemoryOutbox Outbox(ServiceProvider provider)
        {
            return provider.GetRequiredService<InMemoryOutbox>();
        }

        private static OutboxLog Log(ServiceProvider provider)
        {
            return provider.GetRequiredService<OutboxLog>();
        }

        private static Task<OutboxRunResult> Run(ServiceProvider provider)
        {
            return provider.GetRequiredService<IOutboxRunner>().RunOnce(Token);
        }

        private static async Task<T> Maintain<T>(ServiceProvider provider, Func<IOutboxMaintenance, Task<T>> action)
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<IOutboxMaintenance>());
        }

        private static async Task<IHostedService> Start(ServiceProvider provider)
        {
            IHostedService processor = Assert.Single(provider.GetServices<IHostedService>());
            await processor.StartAsync(Token);
            return processor;
        }

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
}
