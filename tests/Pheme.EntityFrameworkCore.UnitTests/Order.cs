namespace Pheme.EntityFrameworkCore.UnitTests
{
    public class Order(string name) : IHasDomainEvents
    {
        private readonly List<object> raised = [];

        public int Id { get; set; }

        public string Name { get; set; } = name;

        public IReadOnlyCollection<object> DomainEvents => raised;

        public void Raise(object domainEvent)
        {
            raised.Add(domainEvent);
        }

        public void ClearDomainEvents()
        {
            raised.Clear();
        }
    }
}
