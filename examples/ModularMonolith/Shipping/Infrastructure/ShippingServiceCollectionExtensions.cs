using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Application;
using Shipping.Application.PrepareShipment;
using Shipping.Domain;

namespace Shipping.Infrastructure
{
    /// <summary>
    /// Registers the shipping module, its handlers included; the composition root adds the pipeline, the outbox and the
    /// contracts.
    /// </summary>
    public static class ShippingServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="ShippingDbContext"/>, publishing the events of <see cref="AggregateRoot"/> entities when it
        /// saves and storing the integration events of <see cref="ShippingModule"/> in its outbox, its handlers and the
        /// carrier.
        /// </summary>
        public static IServiceCollection AddShipping(this IServiceCollection services, string connectionString)
        {
            // The handlers of the module's assembly; the composition root adds the pipeline.
            services.AddPheme(pheme => pheme.AddHandlersFromAssemblyContaining<ShippingModule>());

            // The module's own DbContext publishes the events of its own entity contract. UseOutbox: a second outbox in
            // the application, store "Shipping"; the outbox processor delivers the messages of both.
            services.AddPhemeDbContext<ShippingDbContext>(pheme => pheme
                .CollectDomainEventsFrom<AggregateRoot>(entity => entity.DomainEvents, entity => entity.ClearDomainEvents())
                .UseOutbox<ShippingModule>());

            services.AddDbContext<ShippingDbContext>(options => options.UseNpgsql(connectionString));
            services.AddScoped<ICarrier, LoggingCarrier>();
            return services;
        }
    }
}
