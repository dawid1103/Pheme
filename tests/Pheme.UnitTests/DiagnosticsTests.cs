using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pheme.UnitTests
{
    // Activity and meter listeners are global: tests running in parallel would see each other's messages, and a
    // listener of another test would make an activity appear where none is expected.
    [CollectionDefinition(DisableParallelization = true)]
    public sealed class DiagnosticsCollection;

    [Collection(typeof(DiagnosticsCollection))]
    public class DiagnosticsTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Sent_message_gets_an_activity_named_after_its_type_with_kind_and_type_tags()
        {
            using var activities = new ActivityCollector();
            using ServiceProvider provider = BuildProvider();
            using IServiceScope scope = provider.CreateScope();
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            await sender.Send(new DiagnosedCommand(), Token);
            await sender.Send(new DiagnosedCommandWithResponse(), Token);
            await sender.Send(new DiagnosedQuery(), Token);

            Assert.Collection(
                activities.Stopped,
                activity => AssertSucceeded(activity, "Pheme.UnitTests.DiagnosedCommand", "command"),
                activity => AssertSucceeded(activity, "Pheme.UnitTests.DiagnosedCommandWithResponse", "command"),
                activity => AssertSucceeded(activity, "Pheme.UnitTests.DiagnosedQuery", "query"));

            static void AssertSucceeded(Activity activity, string type, string kind)
            {
                Assert.Equal(PhemeDiagnostics.ActivitySourceName, activity.Source.Name);
                Assert.Equal(type, activity.OperationName);
                Assert.Equal(type, activity.DisplayName);
                Assert.Equal(ActivityKind.Internal, activity.Kind);
                Assert.Equal(type, activity.GetTagItem(PhemeDiagnostics.MessageTypeTagName));
                Assert.Equal(kind, activity.GetTagItem(PhemeDiagnostics.MessageKindTagName));
                Assert.Null(activity.GetTagItem(PhemeDiagnostics.ErrorTypeTagName));
                Assert.Equal(ActivityStatusCode.Unset, activity.Status);
                Assert.True(activity.Duration > TimeSpan.Zero);
            }
        }

        [Fact]
        public async Task Failed_message_activity_has_error_status_with_the_exception_message_and_error_type()
        {
            using var activities = new ActivityCollector();
            using ServiceProvider provider = BuildProvider();

            await Assert.ThrowsAsync<InvalidOperationException>(() => Send(provider, new FailingDiagnosedQuery()));

            Activity activity = Assert.Single(activities.Stopped);
            Assert.Equal("Pheme.UnitTests.FailingDiagnosedQuery", activity.OperationName);
            Assert.Equal("query", activity.GetTagItem(PhemeDiagnostics.MessageKindTagName));
            Assert.Equal("System.InvalidOperationException", activity.GetTagItem(PhemeDiagnostics.ErrorTypeTagName));
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
            Assert.Equal("Stock is empty.", activity.StatusDescription);
        }

        [Fact]
        public async Task Activity_is_current_in_behaviors_and_handler_so_a_nested_message_is_its_child()
        {
            using var activities = new ActivityCollector();
            using ServiceProvider provider = BuildProvider(options => options.AddBehavior(typeof(ActivityRecordingBehavior<,>)));

            await Send(provider, new NestingDiagnosedCommand());

            Assert.Equal(
                [
                    "behavior: Pheme.UnitTests.NestingDiagnosedCommand",
                    "handler: Pheme.UnitTests.NestingDiagnosedCommand",
                    "behavior: Pheme.UnitTests.DiagnosedQuery",
                ],
                provider.GetRequiredService<PipelineLog>().Entries);
            Activity query = activities.Stopped[0];
            Activity command = activities.Stopped[1];
            Assert.Equal("Pheme.UnitTests.DiagnosedQuery", query.OperationName);
            Assert.Equal("Pheme.UnitTests.NestingDiagnosedCommand", command.OperationName);
            Assert.Same(command, query.Parent);
        }

        [Fact]
        public async Task Each_published_domain_event_gets_an_activity_covering_its_handlers()
        {
            using var activities = new ActivityCollector();
            using ServiceProvider provider = BuildProvider();
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>()
                .Publish([new DiagnosedDomainEvent(), new DiagnosedDomainEvent()], Token);

            Assert.Equal(2, activities.Stopped.Count);
            Assert.All(activities.Stopped, activity =>
            {
                Assert.Equal("Pheme.UnitTests.DiagnosedDomainEvent", activity.OperationName);
                Assert.Equal("Pheme.UnitTests.DiagnosedDomainEvent", activity.GetTagItem(PhemeDiagnostics.MessageTypeTagName));
                Assert.Equal("domain_event", activity.GetTagItem(PhemeDiagnostics.MessageKindTagName));
                Assert.Equal(ActivityStatusCode.Unset, activity.Status);
            });
            Assert.Equal(
                ["handler: Pheme.UnitTests.DiagnosedDomainEvent", "handler: Pheme.UnitTests.DiagnosedDomainEvent"],
                provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Failed_domain_event_activity_has_error_status_and_error_type()
        {
            using var activities = new ActivityCollector();
            using ServiceProvider provider = BuildProvider();
            using IServiceScope scope = provider.CreateScope();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish(new FailingDiagnosedDomainEvent(), Token));

            Activity activity = Assert.Single(activities.Stopped);
            Assert.Equal("domain_event", activity.GetTagItem(PhemeDiagnostics.MessageKindTagName));
            Assert.Equal("System.InvalidOperationException", activity.GetTagItem(PhemeDiagnostics.ErrorTypeTagName));
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
            Assert.Equal("Stock is empty.", activity.StatusDescription);
        }

        [Fact]
        public async Task Without_a_listener_no_activity_is_created()
        {
            Activity? outside = Activity.Current;
            using ServiceProvider provider = BuildProvider(options => options.AddBehavior(typeof(ActivityRecordingBehavior<,>)));

            await Send(provider, new NestingDiagnosedCommand());

            Assert.Null(outside);
            Assert.Equal(["behavior: none", "handler: none", "behavior: none"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        [Fact]
        public async Task Duration_is_recorded_in_seconds_with_kind_type_and_error_type_by_the_meter_of_the_meter_factory()
        {
            using ServiceProvider provider = BuildProvider(services: services => services.AddMetrics());
            using var durations = new DurationCollector(provider.GetRequiredService<IMeterFactory>());
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new DiagnosedCommand(), Token);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new FailingDiagnosedQuery(), Token));
            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish(new DiagnosedDomainEvent(), Token);

            Assert.Collection(
                durations.Measurements,
                measurement => AssertMeasurement(measurement, "Pheme.UnitTests.DiagnosedCommand", "command", errorType: null),
                measurement => AssertMeasurement(measurement, "Pheme.UnitTests.FailingDiagnosedQuery", "query", "System.InvalidOperationException"),
                measurement => AssertMeasurement(measurement, "Pheme.UnitTests.DiagnosedDomainEvent", "domain_event", errorType: null));

            static void AssertMeasurement(DurationMeasurement measurement, string type, string kind, string? errorType)
            {
                Assert.Equal("s", measurement.Unit);
                Assert.InRange(measurement.Seconds, 0d, 10d);
                Assert.Equal(type, measurement.Tags[PhemeDiagnostics.MessageTypeTagName]);
                Assert.Equal(kind, measurement.Tags[PhemeDiagnostics.MessageKindTagName]);
                Assert.Equal(errorType, measurement.Tags.GetValueOrDefault(PhemeDiagnostics.ErrorTypeTagName));
                Assert.Equal(errorType is null ? 2 : 3, measurement.Tags.Count);
            }
        }

        [Fact]
        public async Task Without_a_meter_factory_the_meter_is_owned_by_the_service_provider()
        {
            using var durations = new DurationCollector(meterScope: null);
            ServiceProvider provider = BuildProvider();

            await Send(provider, new DiagnosedCommand());
            Assert.False(durations.Completed);
            await provider.DisposeAsync();

            DurationMeasurement measurement = Assert.Single(durations.Measurements);
            Assert.Equal("Pheme.UnitTests.DiagnosedCommand", measurement.Tags[PhemeDiagnostics.MessageTypeTagName]);
            Assert.True(durations.Completed);
        }

        [Fact]
        public async Task Listeners_throwing_when_a_message_or_event_ends_do_not_change_its_outcome()
        {
            using var activities = new ActivityListener
            {
                ShouldListenTo = source => source.Name == PhemeDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _ => throw new FormatException("Activity listener failed."),
            };
            ActivitySource.AddActivityListener(activities);
            using var durations = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == PhemeDiagnostics.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            durations.SetMeasurementEventCallback<double>((_, _, _, _) => throw new FormatException("Meter listener failed."));
            durations.Start();
            using ServiceProvider provider = BuildProvider();
            using IServiceScope scope = provider.CreateScope();

            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new DiagnosedCommand(), Token);
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new FailingDiagnosedQuery(), Token));
            await scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>().Publish(new DiagnosedDomainEvent(), Token);

            Assert.Equal("Stock is empty.", failure.Message);
            Assert.Equal(["handler: Pheme.UnitTests.DiagnosedDomainEvent"], provider.GetRequiredService<PipelineLog>().Entries);
        }

        private static async Task Send(ServiceProvider provider, ICommand command)
        {
            using IServiceScope scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, Token);
        }

        private static async Task Send<TResponse>(ServiceProvider provider, IQuery<TResponse> query)
        {
            using IServiceScope scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(query, Token);
        }

        [Fact]
        public async Task Outbox_delivery_gets_a_consumer_activity_linked_to_its_writer_and_a_duration()
        {
            using var activities = new ActivityCollector();
            using ServiceProvider provider = new ServiceCollection()
                .AddMetrics()
                .AddSingleton<ILoggerFactory>(new TestLoggerFactory())
                .AddSingleton<OutboxLog>()
                .AddPheme(options => options.AddOutbox())
                .AddInMemoryOutboxStore()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, FailingIntegrationEventHandler<OrderCancelledIntegrationEvent>>()
                .BuildServiceProvider();
            using var durations = new DurationCollector(provider.GetRequiredService<IMeterFactory>());
            var outbox = provider.GetRequiredService<InMemoryOutbox>();
            ActivityContext writer;
            using (Activity placeOrder = new Activity("PlaceOrder").Start())
            {
                outbox.Add(provider, new OrderPlacedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "A-1"));
                outbox.Add(provider, new OrderCancelledIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddMinutes(1)));
                writer = placeOrder.Context;
            }

            await provider.GetRequiredService<IOutboxRunner>().RunOnce(Token);

            Assert.Collection(
                activities.Stopped,
                activity =>
                {
                    AssertDelivery(activity, "tests.order-placed");
                    Assert.Null(activity.GetTagItem(PhemeDiagnostics.ErrorTypeTagName));
                    Assert.Equal(ActivityStatusCode.Unset, activity.Status);
                },
                activity =>
                {
                    AssertDelivery(activity, "Pheme.UnitTests.OrderCancelledIntegrationEvent");
                    Assert.Equal("System.InvalidOperationException", activity.GetTagItem(PhemeDiagnostics.ErrorTypeTagName));
                    Assert.Equal(ActivityStatusCode.Error, activity.Status);
                    Assert.Equal("The handler failed.", activity.StatusDescription);
                });
            Assert.Collection(
                durations.Measurements,
                measurement => AssertMeasurement(measurement, "tests.order-placed", errorType: null),
                measurement => AssertMeasurement(measurement, "Pheme.UnitTests.OrderCancelledIntegrationEvent", "System.InvalidOperationException"));

            void AssertDelivery(Activity activity, string type)
            {
                Assert.Equal(type, activity.OperationName);
                Assert.Equal(ActivityKind.Consumer, activity.Kind);
                Assert.Null(activity.ParentId);
                Assert.NotEqual(writer.TraceId, activity.TraceId);
                ActivityLink link = Assert.Single(activity.Links);
                Assert.Equal((writer.TraceId, writer.SpanId), (link.Context.TraceId, link.Context.SpanId));
                Assert.Equal(type, activity.GetTagItem(PhemeDiagnostics.MessageTypeTagName));
                Assert.Equal("integration_event", activity.GetTagItem(PhemeDiagnostics.MessageKindTagName));
                Assert.Equal("Tests", activity.GetTagItem(PhemeDiagnostics.OutboxSourceTagName));
            }

            static void AssertMeasurement(DurationMeasurement measurement, string type, string? errorType)
            {
                Assert.Equal(type, measurement.Tags[PhemeDiagnostics.MessageTypeTagName]);
                Assert.Equal("integration_event", measurement.Tags[PhemeDiagnostics.MessageKindTagName]);
                Assert.Equal(errorType, measurement.Tags.GetValueOrDefault(PhemeDiagnostics.ErrorTypeTagName));
                Assert.False(measurement.Tags.ContainsKey(PhemeDiagnostics.OutboxSourceTagName));
            }
        }

        [Fact]
        public void Outbox_gauges_report_pending_and_parked_messages_and_the_age_of_the_oldest_pending_per_store()
        {
            var time = new TimerCountingTimeProvider();
            using ServiceProvider provider = new ServiceCollection()
                .AddMetrics()
                .AddSingleton<ILoggerFactory>(new TestLoggerFactory())
                .AddSingleton<TimeProvider>(time)
                .AddPheme(options => options.AddOutbox())
                .AddInMemoryOutboxStore()
                .AddInMemoryOutboxStore("Billing")
                .AddSingleton<OutboxLog>()
                .AddTransient<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, RecordingIntegrationEventHandler<OrderPlacedIntegrationEvent>>()
                .AddTransient<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, RecordingIntegrationEventHandler<OrderCancelledIntegrationEvent>>()
                .BuildServiceProvider();
            var outbox = provider.GetRequiredService<InMemoryOutbox>();
            outbox.Add(provider, new OrderPlacedIntegrationEvent(Guid.NewGuid(), time.GetUtcNow().AddSeconds(-90), "A-1"));
            outbox.Add(provider, new OrderPlacedIntegrationEvent(Guid.NewGuid(), time.GetUtcNow().AddSeconds(-30), "A-2"));
            OutboxMessage parked = provider.GetRequiredService<IOutboxMessageFactory>().Create(new OrderCancelledIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch), "Tests");
            parked.AttemptCount = new OutboxOptions().MaxAttempts;
            outbox.Add(parked);

            Assert.Equal(
                [
                    ("pheme.outbox.messages", 2, "Tests", "pending"),
                    ("pheme.outbox.messages", 1, "Tests", "parked"),
                    ("pheme.outbox.messages", 0, "Billing", "pending"),
                    ("pheme.outbox.messages", 0, "Billing", "parked"),
                    ("pheme.outbox.oldest_pending.age", 90, "Tests", null),
                    ("pheme.outbox.oldest_pending.age", 0, "Billing", null),
                ],
                RecordOutboxGauges(provider));
        }

        [Fact]
        public void Outbox_gauges_leave_out_a_store_that_cannot_be_read()
        {
            using ServiceProvider provider = new ServiceCollection()
                .AddMetrics()
                .AddSingleton<ILoggerFactory>(new TestLoggerFactory())
                .AddPheme(options => options.AddOutbox())
                .AddInMemoryOutboxStore()
                .AddInMemoryOutboxStore("Billing")
                .BuildServiceProvider();
            provider.GetRequiredService<InMemoryOutbox>().UnreadableStats.Add("Tests");

            Assert.Equal(
                [
                    ("pheme.outbox.messages", 0, "Billing", "pending"),
                    ("pheme.outbox.messages", 0, "Billing", "parked"),
                    ("pheme.outbox.oldest_pending.age", 0, "Billing", null),
                ],
                RecordOutboxGauges(provider));
        }

        /// <summary>
        /// Starts the outbox, which creates its gauges, and reads them once.
        /// </summary>
        private static List<(string Name, double Value, string? Source, string? State)> RecordOutboxGauges(ServiceProvider provider)
        {
            provider.GetRequiredService<IOutboxRunner>();
            var measurements = new List<(string Name, double Value, string? Source, string? State)>();
            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, meterListener) =>
                {
                    if (instrument.Meter.Scope == provider.GetRequiredService<IMeterFactory>())
                    {
                        meterListener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Add(instrument, value, tags));
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
            listener.Start();
            listener.RecordObservableInstruments();
            return measurements;

            void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            {
                Dictionary<string, object?> tagValues = tags.ToArray().ToDictionary();
                measurements.Add((
                    instrument.Name,
                    value,
                    (string?)tagValues[PhemeDiagnostics.OutboxSourceTagName],
                    (string?)tagValues.GetValueOrDefault(PhemeDiagnostics.OutboxMessageStateTagName)));
            }
        }

        private static ServiceProvider BuildProvider(Action<PhemeOptions>? options = null, Action<IServiceCollection>? services = null)
        {
            IServiceCollection collection = new ServiceCollection()
                .AddSingleton<PipelineLog>()
                .AddTransient<ICommandHandler<DiagnosedCommand>, DiagnosedHandler>()
                .AddTransient<ICommandHandler<DiagnosedCommandWithResponse, Guid>, DiagnosedHandler>()
                .AddTransient<IQueryHandler<DiagnosedQuery, int>, DiagnosedHandler>()
                .AddTransient<IQueryHandler<FailingDiagnosedQuery, int>, DiagnosedHandler>()
                .AddTransient<ICommandHandler<NestingDiagnosedCommand>, NestingDiagnosedHandler>()
                .AddTransient<IDomainEventHandler<DiagnosedDomainEvent>, DiagnosedDomainEventHandler>()
                .AddTransient<IDomainEventHandler<FailingDiagnosedDomainEvent>, DiagnosedDomainEventHandler>();
            services?.Invoke(collection);
            collection.AddPheme(phemeOptions => options?.Invoke(phemeOptions));
            return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }
    }

    public sealed record DiagnosedCommand : ICommand;

    public sealed record DiagnosedCommandWithResponse : ICommand<Guid>;

    public sealed record DiagnosedQuery : IQuery<int>;

    public sealed record FailingDiagnosedQuery : IQuery<int>;

    public sealed record NestingDiagnosedCommand : ICommand;

    public sealed record DiagnosedDomainEvent;

    public sealed record FailingDiagnosedDomainEvent;

    internal sealed class DiagnosedHandler :
        ICommandHandler<DiagnosedCommand>,
        ICommandHandler<DiagnosedCommandWithResponse, Guid>,
        IQueryHandler<DiagnosedQuery, int>,
        IQueryHandler<FailingDiagnosedQuery, int>
    {
        public Task Handle(DiagnosedCommand command, CancellationToken cancellationToken)
        {
            return Task.Delay(1, cancellationToken);
        }

        public Task<Guid> Handle(DiagnosedCommandWithResponse command, CancellationToken cancellationToken)
        {
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<int> Handle(DiagnosedQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(1);
        }

        public async Task<int> Handle(FailingDiagnosedQuery query, CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("Stock is empty.");
        }
    }

    internal sealed class NestingDiagnosedHandler(ISender sender, PipelineLog log) : ICommandHandler<NestingDiagnosedCommand>
    {
        public async Task Handle(NestingDiagnosedCommand command, CancellationToken cancellationToken)
        {
            log.Entries.Add($"handler: {Activity.Current?.OperationName ?? "none"}");
            await sender.Send(new DiagnosedQuery(), cancellationToken);
        }
    }

    internal sealed class DiagnosedDomainEventHandler(PipelineLog log) : IDomainEventHandler<DiagnosedDomainEvent>, IDomainEventHandler<FailingDiagnosedDomainEvent>
    {
        public Task Handle(DiagnosedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            log.Entries.Add($"handler: {Activity.Current?.OperationName ?? "none"}");
            return Task.CompletedTask;
        }

        public Task Handle(FailingDiagnosedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Stock is empty.");
        }
    }

    internal sealed class ActivityRecordingBehavior<TMessage, TResponse>(PipelineLog log) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            log.Entries.Add($"behavior: {Activity.Current?.OperationName ?? "none"}");
            return next(cancellationToken);
        }
    }

    internal sealed class ActivityCollector : IDisposable
    {
        private readonly ActivityListener listener;
        private readonly List<Activity> stopped = [];

        public ActivityCollector()
        {
            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == PhemeDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (stopped)
                    {
                        stopped.Add(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(listener);
        }

        public IReadOnlyList<Activity> Stopped
        {
            get
            {
                lock (stopped)
                {
                    return [.. stopped];
                }
            }
        }

        public void Dispose()
        {
            listener.Dispose();
        }
    }

    internal sealed record DurationMeasurement(string? Unit, double Seconds, IReadOnlyDictionary<string, object?> Tags);

    internal sealed class DurationCollector : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly List<DurationMeasurement> measurements = [];

        /// <param name="meterScope">The meter factory whose meter to listen to, or <see langword="null"/> for any meter.</param>
        public DurationCollector(object? meterScope)
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == PhemeDiagnostics.MeterName
                    && instrument.Name == PhemeDiagnostics.MessageDurationName
                    && (meterScope is null || instrument.Meter.Scope == meterScope))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.MeasurementsCompleted = (_, _) => Completed = true;
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                lock (measurements)
                {
                    measurements.Add(new DurationMeasurement(instrument.Unit, value, tags.ToArray().ToDictionary()));
                }
            });
            listener.Start();
        }

        public bool Completed { get; private set; }

        public IReadOnlyList<DurationMeasurement> Measurements
        {
            get
            {
                lock (measurements)
                {
                    return [.. measurements];
                }
            }
        }

        public void Dispose()
        {
            listener.Dispose();
        }
    }
}
