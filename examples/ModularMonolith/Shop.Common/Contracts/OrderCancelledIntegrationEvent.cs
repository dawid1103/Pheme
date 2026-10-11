using Pheme;

namespace Shop.Common.Contracts
{
    /// <summary>
    /// An order was cancelled. Written to the ordering module's outbox with the cancelled order; the shipping module
    /// stops preparing its shipment, or asks for the parcel back when it was dispatched already.
    /// </summary>
    [IntegrationEventName("ordering.order-cancelled")]
    public sealed record OrderCancelledIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;
}
