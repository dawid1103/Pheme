using Pheme;

namespace Shop.Common.Contracts
{
    /// <summary>
    /// The shipment of an order left the warehouse. Written to the shipping module's outbox with the dispatched shipment;
    /// the ordering module marks the order shipped.
    /// </summary>
    [IntegrationEventName("shipping.shipment-dispatched")]
    public sealed record ShipmentDispatchedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;
}
