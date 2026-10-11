using Ordering.Application.Behaviors;
using Shop.Common.Security;

namespace Ordering.Application.Inventory.ReceiveStock
{
    /// <summary>
    /// Records a delivery of items of a product: its stock grows, and its open restock requests are closed. For the
    /// warehouse staff; handled in a transaction, as the requests are closed by a direct delete. Audited.
    /// </summary>
    public sealed record ReceiveStockCommand(string Product, int Quantity) : ITransactionalCommand, IRequireRole, IAuditedCommand
    {
        /// <inheritdoc />
        public static string RequiredRole => Roles.Warehouse;
    }
}
