namespace Pheme
{
    /// <summary>
    /// An event published by one module for others, delivered through the outbox after the transaction that wrote it
    /// commits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Integration events are messages between modules, not domain types: keep them in a contracts assembly both
    /// sides reference. They are serialized to JSON with their runtime type, so they need a shape that serializes and
    /// deserializes, such as a record with a constructor taking every property.
    /// </para>
    /// <para>
    /// Give each event type a stable name with <see cref="IntegrationEventNameAttribute"/>; without it the name is the
    /// type's full name, and renaming or moving the type breaks the messages still waiting in the outbox.
    /// </para>
    /// </remarks>
    public interface IIntegrationEvent
    {
        /// <summary>
        /// Gets the identifier of this event, unique across all events: the identifier of its outbox message.
        /// </summary>
        /// <remarks>
        /// Delivery is at least once, so a handler can use it to recognize an event it has already handled. Adding the
        /// same event to the outbox twice fails, at the latest when the transaction saves.
        /// </remarks>
        Guid EventId { get; }

        /// <summary>
        /// Gets when the event occurred. The messages of one outbox are tried in the order of this value; a failed one
        /// is tried again later, so the messages after it may be delivered first.
        /// </summary>
        DateTimeOffset OccurredOn { get; }
    }
}
