namespace Ordering.Domain
{
    /// <summary>
    /// An entity that raises domain events, published when the entity is saved.
    /// </summary>
    public interface IHasDomainEvents
    {
        /// <summary>
        /// The events raised since the entity was last saved.
        /// </summary>
        IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

        /// <summary>
        /// Forgets the raised events, once they are published.
        /// </summary>
        void ClearDomainEvents();
    }
}
