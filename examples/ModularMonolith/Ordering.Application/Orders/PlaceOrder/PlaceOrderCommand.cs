using System.Text.Json.Serialization;
using Ordering.Application.Behaviors;

namespace Ordering.Application.Orders.PlaceOrder
{
    /// <summary>
    /// Places an order of <paramref name="Quantity"/> items of <paramref name="Product"/>, delivered by express when
    /// <paramref name="Express"/>; answers the order's identifier. Placed once per idempotency key, and audited.
    /// </summary>
    public sealed record PlaceOrderCommand(string Product, int Quantity, bool Express = false) : IIdempotentCommand<Guid>, IAuditedCommand
    {
        /// <inheritdoc />
        [JsonIgnore]
        public Guid? IdempotencyKey { get; init; }
    }
}
