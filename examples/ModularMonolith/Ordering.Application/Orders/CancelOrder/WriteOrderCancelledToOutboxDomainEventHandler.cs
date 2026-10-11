using Ordering.Domain.Orders.Events;
using Pheme;
using Shop.Common.Contracts;

namespace Ordering.Application.Orders.CancelOrder
{
    internal sealed class WriteOrderCancelledToOutboxDomainEventHandler(
        IOutboxWriter<OrderingModule> outbox,
        TimeProvider timeProvider)
        : IDomainEventHandler<OrderCancelledDomainEvent>
    {
        public Task Handle(OrderCancelledDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            // Saved with the cancelled order, by the SaveChangesAsync that published OrderCancelledDomainEvent.
            outbox.Add(new OrderCancelledIntegrationEvent(Guid.CreateVersion7(), timeProvider.GetUtcNow(), domainEvent.OrderId));
            return Task.CompletedTask;
        }
    }
}
