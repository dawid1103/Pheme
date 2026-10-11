using System.Diagnostics.CodeAnalysis;

namespace Pheme
{
    /// <summary>
    /// Handles a domain event published through <see cref="IDomainEventPublisher"/>.
    /// </summary>
    /// <typeparam name="TEvent">Any application type; no marker interface is required.</typeparam>
    /// <remarks>
    /// <para>
    /// An event may have any number of handlers, none included. A handler receives only events of exactly
    /// <typeparamref name="TEvent"/>, not of derived types. It is resolved from the caller's dependency injection scope
    /// and may be <see langword="internal"/>; one class may handle several events.
    /// </para>
    /// <para>
    /// A handler of an interface or abstract <typeparamref name="TEvent"/> would never run, so the start-up validation
    /// of the registrations reports it. To handle every event of a kind, make the handler generic, constrain its type
    /// parameter to that kind and register it by hand as an open generic:
    /// <c>services.AddTransient(typeof(IDomainEventHandler&lt;&gt;), typeof(AuditDomainEventHandler&lt;&gt;))</c>.
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "A handler of domain events, named like ICommandHandler and IQueryHandler; not a .NET event delegate.")]
    public interface IDomainEventHandler<in TEvent> where TEvent : notnull
    {
        /// <summary>
        /// Handles the domain event.
        /// </summary>
        /// <param name="domainEvent">The event to handle.</param>
        /// <param name="cancellationToken">The token passed by the caller of <see cref="IDomainEventPublisher"/>.</param>
        /// <returns>A task that completes when the event has been handled.</returns>
        Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
    }
}
