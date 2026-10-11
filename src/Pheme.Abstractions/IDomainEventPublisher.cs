using System.Diagnostics.CodeAnalysis;

namespace Pheme
{
    /// <summary>
    /// Publishes domain events to their <see cref="IDomainEventHandler{TEvent}"/> handlers, in-process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An event goes to the handlers of its exact runtime type, one after another, in the order of their service
    /// registrations; an event without handlers is skipped. Several events are published in order, each to all its
    /// handlers before the next one.
    /// </para>
    /// <para>
    /// The first exception thrown by a handler stops the publication and reaches the caller unchanged. Handlers are
    /// resolved from the dependency injection scope this publisher was resolved from.
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple public overloads with optional parameters",
        Justification = "An optional CancellationToken is the .NET convention; interface members cannot be added later without a breaking change anyway.")]
    public interface IDomainEventPublisher
    {
        /// <summary>
        /// Publishes one domain event to the handlers of its runtime type.
        /// </summary>
        /// <param name="domainEvent">The event to publish.</param>
        /// <param name="cancellationToken">A token passed to the handlers.</param>
        /// <returns>A task that completes when all handlers of the event have handled it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="domainEvent"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="domainEvent"/> is a collection, most likely of events meant for
        /// <see cref="Publish(IEnumerable{object}, CancellationToken)"/>.
        /// </exception>
        Task Publish(object domainEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes domain events in order, each to the handlers of its runtime type.
        /// </summary>
        /// <param name="domainEvents">
        /// The events to publish. Copied before the first event is published, so a handler may add events to the source
        /// collection; the added events are not published by this call.
        /// </param>
        /// <param name="cancellationToken">A token passed to the handlers.</param>
        /// <returns>A task that completes when all handlers of all events have handled them.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="domainEvents"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="domainEvents"/> contains <see langword="null"/> or a collection. Checked before any event
        /// is published.
        /// </exception>
        Task Publish(IEnumerable<object> domainEvents, CancellationToken cancellationToken = default);
    }
}
