namespace Ordering.Domain.Orders
{
    /// <summary>
    /// The state of an <see cref="Order"/>.
    /// </summary>
    public enum OrderStatus
    {
        /// <summary>
        /// Placed by the customer, waiting for shipping.
        /// </summary>
        Placed,

        /// <summary>
        /// Cancelled before it was shipped, or while the shipping module was dispatching it.
        /// </summary>
        Cancelled,

        /// <summary>
        /// Dispatched by the shipping module; no longer cancellable.
        /// </summary>
        Shipped,
    }
}
