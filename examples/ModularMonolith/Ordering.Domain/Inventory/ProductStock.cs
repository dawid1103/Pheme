using Ordering.Domain.Inventory.Events;

namespace Ordering.Domain.Inventory
{
    /// <summary>
    /// The items of a product available for orders. Products without a stock are not counted.
    /// </summary>
    public sealed class ProductStock : IHasDomainEvents
    {
        private readonly List<IDomainEvent> domainEvents = [];

        private ProductStock(string product, int available, int reorderLevel)
        {
            Product = product;
            Available = available;
            ReorderLevel = reorderLevel;
        }

        /// <summary>
        /// The product, the identifier of its stock.
        /// </summary>
        public string Product { get; private set; }

        /// <summary>
        /// The number of items not allocated to orders yet.
        /// </summary>
        public int Available { get; private set; }

        /// <summary>
        /// The number of available items at which the stock is running low.
        /// </summary>
        public int ReorderLevel { get; private set; }

        /// <inheritdoc />
        public IReadOnlyCollection<IDomainEvent> DomainEvents => domainEvents.AsReadOnly();

        /// <summary>
        /// Starts counting the items of a product.
        /// </summary>
        public static ProductStock Create(string product, int available, int reorderLevel)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(product);
            ArgumentOutOfRangeException.ThrowIfNegative(available);
            ArgumentOutOfRangeException.ThrowIfNegative(reorderLevel);

            return new ProductStock(product, available, reorderLevel);
        }

        /// <summary>
        /// Tells whether <paramref name="quantity"/> items are available.
        /// </summary>
        public bool CanAllocate(int quantity) => quantity <= Available;

        /// <summary>
        /// Allocates items to an order; raises <see cref="ReorderLevelReachedDomainEvent"/> when that brings the stock
        /// down to its reorder level.
        /// </summary>
        public void Allocate(int quantity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
            if (!CanAllocate(quantity))
            {
                throw new InvalidOperationException($"Only {Available} items of {Product} are available, not {quantity}.");
            }

            bool wasAboveReorderLevel = Available > ReorderLevel;
            Available -= quantity;
            if (wasAboveReorderLevel && Available <= ReorderLevel)
            {
                domainEvents.Add(new ReorderLevelReachedDomainEvent(Product, Available));
            }
        }

        /// <summary>
        /// Adds delivered items to the stock.
        /// </summary>
        public void Receive(int quantity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
            Available += quantity;
        }

        /// <inheritdoc />
        public void ClearDomainEvents() => domainEvents.Clear();
    }
}
