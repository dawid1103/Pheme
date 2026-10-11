namespace Ordering.Domain.Inventory.Events
{
    /// <summary>
    /// Raised when allocating items brings a <see cref="ProductStock"/> down to its reorder level, with the items
    /// <paramref name="Available"/> after the allocation.
    /// </summary>
    public sealed record ReorderLevelReachedDomainEvent(string Product, int Available) : IDomainEvent;
}
