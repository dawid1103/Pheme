namespace Ordering.Domain.Orders
{
    /// <summary>
    /// One entry of an order's history, numbered from 1 in the order the entries were recorded.
    /// </summary>
    public sealed class OrderHistoryEntry
    {
        private OrderHistoryEntry(Guid orderId, int sequence, string description)
        {
            OrderId = orderId;
            Sequence = sequence;
            Description = description;
        }

        /// <summary>
        /// The identifier of the entry.
        /// </summary>
        public Guid Id { get; private set; } = Guid.CreateVersion7();

        /// <summary>
        /// The identifier of the order.
        /// </summary>
        public Guid OrderId { get; private set; }

        /// <summary>
        /// The number of the entry within the order's history.
        /// </summary>
        public int Sequence { get; private set; }

        /// <summary>
        /// What happened to the order.
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        /// Records the entry number <paramref name="sequence"/> of an order's history.
        /// </summary>
        public static OrderHistoryEntry Record(Guid orderId, int sequence, string description)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
            ArgumentException.ThrowIfNullOrWhiteSpace(description);

            return new OrderHistoryEntry(orderId, sequence, description);
        }
    }
}
