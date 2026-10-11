using Pheme;

namespace Shop.Common.Contracts
{
    /// <summary>
    /// An order was placed. Written to the ordering module's outbox in the transaction of the order and delivered after
    /// it is saved, to a handler in each module: shipping prepares the shipment, ordering confirms the order to the
    /// customer.
    /// </summary>
    /// <remarks>
    /// The stored name keeps the messages already in the outbox readable when the type moves to another namespace or
    /// assembly. <see cref="Express"/> was added after the first version: a message written before reads
    /// <see langword="false"/>, standard delivery, which is what every order was then. A change the messages in the
    /// outbox cannot read this way is a new event with a new name (docs/guide/09-operating-the-outbox.md, "Changing an
    /// integration event").
    /// </remarks>
    [IntegrationEventName("ordering.order-placed")]
    public sealed record OrderPlacedIntegrationEvent(
        Guid EventId,
        DateTimeOffset OccurredOn,
        Guid OrderId,
        string Product,
        int Quantity,
        bool Express)
        : IIntegrationEvent;
}
