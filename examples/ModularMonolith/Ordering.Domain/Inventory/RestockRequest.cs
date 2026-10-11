namespace Ordering.Domain.Inventory
{
    /// <summary>
    /// A request to the purchasing staff for more items of a product whose stock is running low.
    /// </summary>
    public sealed class RestockRequest
    {
        private RestockRequest(string product, int available)
        {
            Product = product;
            Available = available;
        }

        /// <summary>
        /// The identifier of the request.
        /// </summary>
        public Guid Id { get; private set; } = Guid.CreateVersion7();

        /// <summary>
        /// The product to restock.
        /// </summary>
        public string Product { get; private set; }

        /// <summary>
        /// The items of the product available when the request was made.
        /// </summary>
        public int Available { get; private set; }

        /// <summary>
        /// Requests more items of a product, of which <paramref name="available"/> are left.
        /// </summary>
        public static RestockRequest Create(string product, int available)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(product);
            ArgumentOutOfRangeException.ThrowIfNegative(available);

            return new RestockRequest(product, available);
        }
    }
}
