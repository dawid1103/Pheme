using System.Collections.Concurrent;

namespace Pheme.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    /// What the integration event handlers of a test handled, and how many more times the failing ones fail.
    /// </summary>
    internal sealed class Deliveries
    {
        public ConcurrentQueue<Guid> Handled { get; } = new();

        public int FailuresLeft { get; set; }
    }

    // Generic, so the scan of this assembly by the domain event tests skips them; registered by hand, closed.

    /// <summary>
    /// Does nothing, so every event the tests write has a handler, as the outbox requires.
    /// </summary>
    internal sealed class IgnoringIntegrationEventHandler<TEvent> : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingIntegrationEventHandler<TEvent>(Deliveries deliveries) : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            deliveries.Handled.Enqueue(integrationEvent.EventId);
            return Task.CompletedTask;
        }
    }

    internal sealed class FlakyIntegrationEventHandler<TEvent>(Deliveries deliveries) : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            if (deliveries.FailuresLeft > 0)
            {
                deliveries.FailuresLeft--;
                throw new InvalidOperationException("The service is down.");
            }

            deliveries.Handled.Enqueue(integrationEvent.EventId);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Records an event and writes a <see cref="ParcelSentIntegrationEvent"/> to the outbox of the ordering context in
    /// reply, as a handler of one module starting the work of another.
    /// </summary>
    internal sealed class ReplyingIntegrationEventHandler<TEvent>(Deliveries deliveries, IOutboxWriter<OrderingModule> writer, OrderingDbContext context)
        : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public async Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            deliveries.Handled.Enqueue(integrationEvent.EventId);
            writer.Add(new ParcelSentIntegrationEvent(Guid.NewGuid(), integrationEvent.OccurredOn.AddMinutes(1)));
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Adds a note to the context of its scope and fails before saving it.
    /// </summary>
    internal sealed class NoteThenFailIntegrationEventHandler<TEvent>(OrderingDbContext context) : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            context.Notes.Add(new Note("note of the failed handler"));
            throw new InvalidOperationException("The handler failed.");
        }
    }

    /// <summary>
    /// Adds a note to the context of its scope and saves it.
    /// </summary>
    internal sealed class NoteIntegrationEventHandler<TEvent>(OrderingDbContext context) : IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        public async Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            context.Notes.Add(new Note("note of the saving handler"));
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
