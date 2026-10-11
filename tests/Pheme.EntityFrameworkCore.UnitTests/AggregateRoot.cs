namespace Pheme.EntityFrameworkCore.UnitTests
{
    /// <summary>
    /// A base class of entities raising events, collected separately from <see cref="IHasDomainEvents"/>.
    /// </summary>
    public abstract class AggregateRoot
    {
        private readonly List<object> raised = [];

        public IReadOnlyList<object> Events => raised;

        public void Raise(object domainEvent)
        {
            raised.Add(domainEvent);
        }

        public void ClearEvents()
        {
            raised.Clear();
        }
    }
}
