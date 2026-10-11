namespace Pheme
{
    internal sealed class DomainEventCollector(
        Type entityType,
        Func<object, IEnumerable<object>?> getDomainEvents,
        Action<object> clearDomainEvents)
    {
        public Type EntityType { get; } = entityType;

        public IEnumerable<object>? GetDomainEvents(object entity)
        {
            return getDomainEvents(entity);
        }

        public void ClearDomainEvents(object entity)
        {
            clearDomainEvents(entity);
        }
    }
}
