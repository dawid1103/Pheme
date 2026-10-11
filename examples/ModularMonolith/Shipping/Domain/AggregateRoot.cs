namespace Shipping.Domain
{
    /// <summary>
    /// Base class of the shipping aggregates: collects the domain events raised until the aggregate is saved.
    /// </summary>
    public abstract class AggregateRoot
    {
        private readonly List<IDomainEvent> domainEvents = [];

        /// <summary>
        /// The identifier of the aggregate.
        /// </summary>
        public Guid Id { get; protected init; } = Guid.CreateVersion7();

        /// <summary>
        /// The events raised since the aggregate was last saved.
        /// </summary>
        public IReadOnlyList<IDomainEvent> DomainEvents => domainEvents.AsReadOnly();

        /// <summary>
        /// Forgets the raised events, once they are published.
        /// </summary>
        public void ClearDomainEvents() => domainEvents.Clear();

        /// <summary>
        /// Records <paramref name="domainEvent"/>, to be published when the aggregate is saved.
        /// </summary>
        protected void Raise(IDomainEvent domainEvent) => domainEvents.Add(domainEvent);
    }
}
