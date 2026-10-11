using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Inventory;
using Ordering.Application.Orders.History;
using Ordering.Application.Orders.PlaceOrder;
using Ordering.Domain.Orders.Events;
using Pheme;

namespace Ordering.Application
{
    /// <summary>
    /// Registers the ordering handlers whose order <c>AddPheme</c> must not choose by itself.
    /// </summary>
    public static class OrderingApplicationServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the handlers of <see cref="OrderPlacedDomainEvent"/> in the order they run: the history, the
        /// integration event for the other modules, then the stock allocation. Call it before <c>AddPheme</c>, which
        /// keeps these first; its scan alone would order them by full type name, allocating the stock first.
        /// </summary>
        /// <remarks>
        /// The integration event goes to the outbox before the allocation, which may still fail: the outbox saves it
        /// with the order or not at all, so the order of these handlers does not matter to it.
        /// </remarks>
        public static IServiceCollection AddOrderingApplication(this IServiceCollection services)
        {
            services.AddTransient<IDomainEventHandler<OrderPlacedDomainEvent>, RecordOrderHistoryDomainEventHandler>();
            services.AddTransient<IDomainEventHandler<OrderPlacedDomainEvent>, WriteOrderPlacedToOutboxDomainEventHandler>();
            services.AddTransient<IDomainEventHandler<OrderPlacedDomainEvent>, AllocateStockDomainEventHandler>();
            return services;
        }
    }
}
