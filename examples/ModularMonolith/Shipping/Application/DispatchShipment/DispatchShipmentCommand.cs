using Pheme;
using Shop.Common.Security;

namespace Shipping.Application.DispatchShipment
{
    /// <summary>
    /// Hands the shipment of an order to the carrier, for the warehouse staff; the ordering module learns of it through
    /// the outbox.
    /// </summary>
    public sealed record DispatchShipmentCommand(Guid OrderId) : ICommand, IRequireRole
    {
        /// <inheritdoc />
        public static string RequiredRole => Roles.Warehouse;
    }
}
