namespace Shipping.Domain.Events
{
    /// <summary>
    /// Raised when the preparation of a <see cref="Shipment"/> is cancelled.
    /// </summary>
    public sealed record ShipmentPreparationCancelledDomainEvent(Guid ShipmentId, Guid OrderId) : IDomainEvent;
}
