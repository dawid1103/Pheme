namespace Pheme.UnitTests
{
    [IntegrationEventName("tests.order-placed")]
    internal sealed record OrderPlacedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, string OrderNumber) : IIntegrationEvent;

    internal sealed record OrderCancelledIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

    internal record PaymentReceivedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

    internal sealed record LargePaymentReceivedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, decimal Amount) : PaymentReceivedIntegrationEvent(EventId, OccurredOn);

    [IntegrationEventName(" ")]
    internal sealed record UnnamedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

    [IntegrationEventName("tests.order-placed")]
    internal sealed record OrderPlacedTwinIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

    internal sealed record EnvelopeIntegrationEvent<TPayload>(Guid EventId, DateTimeOffset OccurredOn, TPayload Payload) : IIntegrationEvent;

    internal abstract record AbstractIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

    /// <summary>
    /// The events handled by the integration event handlers, with the handler that received each.
    /// </summary>
    internal sealed class OutboxLog
    {
        public List<(string Handler, IIntegrationEvent Event)> Deliveries { get; } = [];

        public void Add(object handler, IIntegrationEvent integrationEvent)
        {
            Deliveries.Add((handler.GetType().Name, integrationEvent));
        }
    }

    /// <summary>
    /// The only concrete integration event handler of the test assembly, found by the scan; the generic handlers below
    /// are registered by hand.
    /// </summary>
    internal sealed class OrderIntegrationEventHandler(OutboxLog log) : IIntegrationEventHandler<OrderPlacedIntegrationEvent>, IIntegrationEventHandler<OrderCancelledIntegrationEvent>
    {
        public Task Handle(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            log.Add(this, integrationEvent);
            return Task.CompletedTask;
        }

        public Task Handle(OrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            log.Add(this, integrationEvent);
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingIntegrationEventHandler<TEvent>(OutboxLog log) : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            log.Add(this, integrationEvent);
            return Task.CompletedTask;
        }
    }

    internal sealed class SecondRecordingIntegrationEventHandler<TEvent>(OutboxLog log) : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            log.Add(this, integrationEvent);
            return Task.CompletedTask;
        }
    }

    internal sealed class FailingIntegrationEventHandler<TEvent>(OutboxLog log) : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            log.Add(this, integrationEvent);
            throw new InvalidOperationException("The handler failed.");
        }
    }

    /// <summary>
    /// Adds work to the unit of work of its scope and fails, as a handler whose tracked changes must not be saved.
    /// </summary>
    internal sealed class FailingWorkIntegrationEventHandler<TEvent>(UnitOfWork work) : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            work.Pending.Add("work of the failed handler");
            throw new InvalidOperationException("The handler failed.");
        }
    }

    /// <summary>
    /// Cancels the run it is called in, as a shutdown would, and fails on the cancelled token.
    /// </summary>
    internal sealed class CancellingIntegrationEventHandler<TEvent>(CancellationTokenSource run) : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public async Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            await run.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    internal sealed class CancelledIntegrationEventHandler<TEvent> : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
