using Ordering.Domain.Orders.Events;

namespace Ordering.Domain.Orders
{
    /// <summary>
    /// An order of one product: the aggregate of the ordering module.
    /// </summary>
    public sealed class Order : IHasDomainEvents
    {
        private readonly List<IDomainEvent> domainEvents = [];

        private Order(Guid id, string product, int quantity, bool express)
        {
            Id = id;
            Product = product;
            Quantity = quantity;
            Express = express;
        }

        /// <summary>
        /// The identifier of the order.
        /// </summary>
        public Guid Id { get; private set; }

        /// <summary>
        /// The ordered product.
        /// </summary>
        public string Product { get; private set; }

        /// <summary>
        /// The number of ordered items, at least one.
        /// </summary>
        public int Quantity { get; private set; }

        /// <summary>
        /// Whether the customer chose express delivery.
        /// </summary>
        public bool Express { get; private set; }

        /// <summary>
        /// The state of the order.
        /// </summary>
        public OrderStatus Status { get; private set; }

        /// <inheritdoc />
        public IReadOnlyCollection<IDomainEvent> DomainEvents => domainEvents.AsReadOnly();

        /// <summary>
        /// Places an order and raises <see cref="OrderPlacedDomainEvent"/>.
        /// </summary>
        public static Order Place(string product, int quantity, bool express)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(product);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

            var order = new Order(Guid.CreateVersion7(), product, quantity, express) { Status = OrderStatus.Placed };
            order.domainEvents.Add(new OrderPlacedDomainEvent(order.Id, product, quantity, express));
            return order;
        }

        /// <summary>
        /// Tells whether the order may still be cancelled: it is not shipped.
        /// </summary>
        public bool CanCancel => Status != OrderStatus.Shipped;

        /// <summary>
        /// Cancels the order and raises <see cref="OrderCancelledDomainEvent"/>; cancelling a cancelled order changes
        /// nothing.
        /// </summary>
        /// <exception cref="InvalidOperationException">The order is shipped (<see cref="CanCancel"/>).</exception>
        public void Cancel()
        {
            if (!CanCancel)
            {
                throw new InvalidOperationException($"Order {Id} is shipped and can no longer be cancelled.");
            }

            if (Status == OrderStatus.Cancelled)
            {
                return;
            }

            Status = OrderStatus.Cancelled;
            domainEvents.Add(new OrderCancelledDomainEvent(Id));
        }

        /// <summary>
        /// Records that the shipping module dispatched the order and raises <see cref="OrderShippedDomainEvent"/>;
        /// changes nothing when the order is shipped already, or cancelled - the cancellation reaches the shipping module,
        /// which asks for the parcel back.
        /// </summary>
        public void MarkShipped()
        {
            if (Status != OrderStatus.Placed)
            {
                return;
            }

            Status = OrderStatus.Shipped;
            domainEvents.Add(new OrderShippedDomainEvent(Id));
        }

        /// <inheritdoc />
        public void ClearDomainEvents() => domainEvents.Clear();
    }
}
