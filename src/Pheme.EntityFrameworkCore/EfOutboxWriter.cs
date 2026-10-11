using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pheme
{
    /// <summary>
    /// Adds the integration events of <typeparamref name="TModule"/> to the outbox table of <typeparamref name="TContext"/>
    /// of the current scope, saved by its next <c>SaveChanges</c>.
    /// </summary>
    internal sealed class EfOutboxWriter<TContext, TModule> : IOutboxWriter<TModule>
        where TContext : DbContext
    {
        private readonly TContext context;
        private readonly DbContextRegistration<TContext> registration;
        private readonly IOutboxMessageFactory factory;

        public EfOutboxWriter(
            TContext context,
            DbContextRegistration<TContext> registration,
            IOptions<OutboxValidationOptions> validation,
            IServiceProvider serviceProvider)
        {
            this.context = context;
            this.registration = registration;

            // Without a host the startup validation does not run; its errors instead of the container's.
            _ = validation.Value;
            factory = serviceProvider.GetRequiredService<IOutboxMessageFactory>();
        }

        public void Add(IIntegrationEvent integrationEvent)
        {
            ArgumentNullException.ThrowIfNull(integrationEvent);

            DbContextRegistration<TContext>.OutboxMessages(context).Add(factory.Create(integrationEvent, registration.OutboxSource));
        }
    }
}
