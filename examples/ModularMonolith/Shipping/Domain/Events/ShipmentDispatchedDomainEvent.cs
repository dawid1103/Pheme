namespace Shipping.Domain.Events
{
    /// <summary>
    /// Raised when a <see cref="Shipment"/> is handed to the carrier.
    /// </summary>
    public sealed record ShipmentDispatchedDomainEvent(Guid ShipmentId, Guid OrderId) : IDomainEvent;
}
