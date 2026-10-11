namespace Shipping.Domain.Events
{
    /// <summary>
    /// Raised when the order of a dispatched <see cref="Shipment"/> is cancelled, so the parcel is to come back.
    /// </summary>
    public sealed record ShipmentReturnRequestedDomainEvent(Guid ShipmentId, Guid OrderId) : IDomainEvent;
}
