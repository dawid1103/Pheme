using Ordering.Domain.Orders.Events;
using Pheme;
using Shop.Common.Contracts;

namespace Ordering.Application.Orders.PlaceOrder
{
    internal sealed class WriteOrderPlacedToOutboxDomainEventHandler(
        IOutboxWriter<OrderingModule> outbox,
        TimeProvider timeProvider)
        : IDomainEventHandler<OrderPlacedDomainEvent>
    {
        public Task Handle(OrderPlacedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            // Not saved here: the SaveChangesAsync that published OrderPlacedDomainEvent saves the message with the
            // order, in the same transaction, so a handler failing after this one saves neither.
            outbox.Add(new OrderPlacedIntegrationEvent(
                Guid.CreateVersion7(),
                timeProvider.GetUtcNow(),
                domainEvent.OrderId,
                domainEvent.Product,
                domainEvent.Quantity,
                domainEvent.Express));
            return Task.CompletedTask;
        }
    }
}
