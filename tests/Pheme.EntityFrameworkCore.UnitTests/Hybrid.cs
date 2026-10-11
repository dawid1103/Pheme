namespace Pheme.EntityFrameworkCore.UnitTests
{
    /// <summary>
    /// An entity matching both registered contracts, so its events would be collected twice.
    /// </summary>
    public class Hybrid(string name) : AggregateRoot, IHasDomainEvents
    {
        public int Id { get; set; }

        public string Name { get; set; } = name;

        public IReadOnlyCollection<object> DomainEvents => Events;

        public void ClearDomainEvents()
        {
            ClearEvents();
        }
    }
}
