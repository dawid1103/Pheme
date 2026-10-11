using System.ComponentModel;

namespace Pheme
{
    /// <summary>
    /// Creates the outbox message of an integration event, so every outbox writer stores events in the same format.
    /// </summary>
    /// <remarks>
    /// For store implementations, not for application code: an <see cref="IOutboxWriter{TModule}"/> creates the message
    /// of each added event with this factory and only saves it. Registered as a singleton by <c>AddOutbox</c> of
    /// <c>PhemeOptions</c>.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public interface IOutboxMessageFactory
    {
        /// <summary>
        /// Creates the pending outbox message of an integration event.
        /// </summary>
        /// <param name="integrationEvent">The event to store.</param>
        /// <param name="source">The <see cref="IOutboxStore.Source"/> of the store the message is written to.</param>
        /// <returns>
        /// A message with the event's <see cref="IIntegrationEvent.EventId"/> as <see cref="OutboxMessage.Id"/>, the
        /// name of its runtime type as <see cref="OutboxMessage.Type"/>, the event serialized to JSON with that type as
        /// <see cref="OutboxMessage.Content"/>, <see cref="IIntegrationEvent.OccurredOn"/> converted to UTC, and the
        /// W3C trace context of the current activity, if any, as <see cref="OutboxMessage.TraceParent"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="integrationEvent"/> or <paramref name="source"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="source"/> is empty or white space.</exception>
        /// <exception cref="InvalidOperationException">
        /// The runtime type of <paramref name="integrationEvent"/> is generic, its
        /// <see cref="IntegrationEventNameAttribute"/> has an empty name, or no
        /// <see cref="IIntegrationEventHandler{TEvent}"/> of it is registered, so the outbox would park the message.
        /// </exception>
        OutboxMessage Create(IIntegrationEvent integrationEvent, string source);
    }
}
