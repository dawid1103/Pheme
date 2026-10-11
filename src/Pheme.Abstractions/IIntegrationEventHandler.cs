using System.Diagnostics.CodeAnalysis;

namespace Pheme
{
    /// <summary>
    /// Handles an integration event delivered from the outbox.
    /// </summary>
    /// <typeparam name="TEvent">The type of the handled event.</typeparam>
    /// <remarks>
    /// <para>
    /// An event may have several handlers; each is called, one after another. A handler that throws does not stop the
    /// others, unless the delivery is being cancelled: the message is tried again later and only the handlers that have
    /// not completed are called again. After the outbox's maximum number of attempts the message is parked and kept for
    /// inspection.
    /// </para>
    /// <para>
    /// Delivery is at least once: a handler may receive the same event again, for example when the application stops
    /// between the handler's work and the outbox recording it. Make handlers idempotent, for example by
    /// <see cref="IIntegrationEvent.EventId"/>.
    /// </para>
    /// <para>
    /// A handler receives only events of exactly <typeparamref name="TEvent"/>, not of derived types. Each message is
    /// handled in its own dependency injection scope, outside of any request; the handler may be
    /// <see langword="internal"/>, and one class may handle several events.
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "A handler of integration events, named like IDomainEventHandler; not a .NET event delegate.")]
    public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
    {
        /// <summary>
        /// Handles the integration event.
        /// </summary>
        /// <param name="integrationEvent">The event to handle.</param>
        /// <param name="cancellationToken">
        /// A token cancelled when the delivery stops, for example when the application stops.
        /// </param>
        /// <returns>A task that completes when the event has been handled.</returns>
        Task Handle(TEvent integrationEvent, CancellationToken cancellationToken);
    }
}
