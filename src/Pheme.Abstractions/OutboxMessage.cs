namespace Pheme
{
    /// <summary>
    /// An integration event stored in the outbox, with the state of its delivery.
    /// </summary>
    /// <remarks>
    /// Created by <see cref="IOutboxWriter{TModule}"/> and read and updated through <see cref="IOutboxStore"/>;
    /// applications only read it, for example when inspecting parked messages.
    /// </remarks>
    public sealed class OutboxMessage
    {
        /// <summary>
        /// Gets or sets the identifier of the message: the <see cref="IIntegrationEvent.EventId"/> of its event.
        /// </summary>
        public required Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the name of the event's type: its <see cref="IntegrationEventNameAttribute"/> name, otherwise
        /// its full name.
        /// </summary>
        public required string Type { get; set; }

        /// <summary>
        /// Gets or sets the event serialized to JSON with its runtime type.
        /// </summary>
        public required string Content { get; set; }

        /// <summary>
        /// Gets or sets the name of the store the message was written to, which identifies the outbox it belongs to.
        /// </summary>
        public required string Source { get; set; }

        /// <summary>
        /// Gets or sets when the event occurred: its <see cref="IIntegrationEvent.OccurredOn"/>, in UTC. The due
        /// messages of one store are tried in the order of this value, then of <see cref="Id"/>.
        /// </summary>
        public required DateTimeOffset OccurredOnUtc { get; set; }

        /// <summary>
        /// Gets or sets when all handlers of the event completed, in UTC; <see langword="null"/> while it is pending
        /// or parked.
        /// </summary>
        public DateTimeOffset? ProcessedOnUtc { get; set; }

        /// <summary>
        /// Gets or sets the error of the last failed attempt; <see langword="null"/> when none failed or the message
        /// has been processed since.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Gets or sets how many attempts to deliver the message have failed. A message whose count reaches the
        /// outbox's maximum number of attempts is parked: no longer delivered, kept for inspection.
        /// </summary>
        public int AttemptCount { get; set; }

        /// <summary>
        /// Gets or sets when the next attempt to deliver the message is due, in UTC: set by the outbox when an attempt
        /// fails, after a delay growing with the attempts; <see langword="null"/> when the message is due at once, as a
        /// new or requeued one, or parked. The message is not tried before.
        /// </summary>
        public DateTimeOffset? NextAttemptOnUtc { get; set; }

        /// <summary>
        /// Gets or sets the handlers that have already handled the event, written by the outbox when an attempt fails
        /// and skipped by the next attempt; <see langword="null"/> when none has.
        /// </summary>
        /// <remarks>
        /// One full type name of a handler class per line, such as <c>Shop.Shipping.CreateShipmentHandler</c>, in C#
        /// notation for generic types and with a <c>+</c> for a nested class, as in <see cref="System.Type.FullName"/>
        /// (<c>Shop.Shipping.PrepareShipment+Handler</c>). A handler whose class is renamed counts as not completed.
        /// </remarks>
        public string? CompletedHandlers { get; set; }

        /// <summary>
        /// Gets or sets the W3C trace context of the activity that wrote the message, so its delivery links to it;
        /// <see langword="null"/> when there was none.
        /// </summary>
        public string? TraceParent { get; set; }
    }
}
