using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application;
using Ordering.Application.Orders.Confirmation;
using Ordering.Domain;

namespace Ordering.Infrastructure
{
    /// <summary>
    /// Registers the ordering module, its handlers included; the composition root adds the pipeline, the outbox and the
    /// contracts.
    /// </summary>
    public static class OrderingServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="OrderingDbContext"/>, publishing the events of <see cref="IHasDomainEvents"/> entities
        /// when it saves and storing the integration events of <see cref="OrderingModule"/> in its outbox, and the
        /// handlers of the module: first the ones whose order matters, then the scanned ones. The composition root's
        /// <c>AddPheme</c> adds the delivery of the outbox (<c>AddOutbox</c>).
        /// </summary>
        public static IServiceCollection AddOrdering(this IServiceCollection services, string connectionString)
        {
            // Before AddPheme, which keeps these handlers ahead of the scanned ones.
            services.AddOrderingApplication();
            services.AddPheme(pheme => pheme.AddHandlersFromAssemblyContaining<OrderingModule>());

            services.AddDbContext<OrderingDbContext>(options => options.UseNpgsql(connectionString));

            // The domain's own contract: Pheme is told how to read and clear the events, the domain knows nothing of it.
            // Before or after AddDbContext alike; the shipping module registers it before.
            // UseOutbox: IOutboxWriter<OrderingModule> writes to this context, whose outbox store is named "Ordering".
            services.AddPhemeDbContext<OrderingDbContext>(pheme => pheme
                .CollectDomainEventsFrom<IHasDomainEvents>(entity => entity.DomainEvents, entity => entity.ClearDomainEvents())
                .UseOutbox<OrderingModule>());
            services.AddScoped<IOrderingDbContext>(provider => provider.GetRequiredService<OrderingDbContext>());
            services.AddScoped<ICustomerNotifier, LoggingCustomerNotifier>();
            return services;
        }
    }
}
