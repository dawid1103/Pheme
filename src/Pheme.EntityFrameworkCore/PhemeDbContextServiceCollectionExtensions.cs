using Microsoft.EntityFrameworkCore;
using Pheme;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Configures what Pheme does with an Entity Framework Core <c>DbContext</c>.
    /// </summary>
    public static class PhemeDbContextServiceCollectionExtensions
    {
        /// <summary>
        /// Configures what Pheme does with <typeparamref name="TContext"/>: it publishes the domain events of its
        /// tracked entities before each <c>SaveChangesAsync</c>, collected from the entity types the configuration
        /// names, and lets the modules the configuration names write integration events to its outbox.
        /// </summary>
        /// <typeparam name="TContext">The <see cref="DbContext"/> Pheme works with.</typeparam>
        /// <param name="services">The service collection to add to.</param>
        /// <param name="configure">Configures the context, see <see cref="PhemeDbContextOptions"/>; called at once.</param>
        /// <returns>The same service collection, for chaining.</returns>
        /// <remarks>
        /// <para>
        /// This call does not register the context itself: that is <c>AddDbContext</c>, before or after this call. Each
        /// context opts in with its own call and its own configuration: a context without one publishes nothing.
        /// Calling it again for the same context adds to its configuration.
        /// </para>
        /// <para>
        /// A context that collects domain events gets an interceptor, added to its options through
        /// <c>ConfigureDbContext</c>. Events are published through <see cref="IDomainEventPublisher"/> of the scope the
        /// context is created in, so handlers share the context and their changes, including new events, are saved in
        /// one transaction. Events are cleared from the entities before they are published; a handler exception reaches
        /// the caller unchanged and nothing is saved. The composition root must also call <c>AddPheme</c>.
        /// </para>
        /// <para>
        /// A synchronous <c>SaveChanges</c> with events to publish throws an <see cref="InvalidOperationException"/>,
        /// since handlers are asynchronous. Requires the default scoped options of <c>AddDbContext</c>: options built
        /// outside a scope would publish through the root service provider. A context that collects domain events and
        /// whose <c>DbContextOptions&lt;TContext&gt;</c> or <c>IDbContextFactory&lt;TContext&gt;</c> is not scoped
        /// (<c>AddDbContextPool</c>, <c>AddPooledDbContextFactory</c>, <c>AddDbContextFactory</c> without
        /// <c>ServiceLifetime.Scoped</c>, another <c>optionsLifetime</c> of <c>AddDbContext</c>) fails the start with an
        /// <see cref="Microsoft.Extensions.Options.OptionsValidationException"/>; without a host, which validates on
        /// start, its options fail when they are built.
        /// </para>
        /// <para>
        /// A module named with <see cref="PhemeDbContextOptions.UseOutbox{TModule}"/> gets an
        /// <see cref="IOutboxWriter{TModule}"/> bound to this context, and the context gets one <see cref="IOutboxStore"/>
        /// for the outbox of Pheme to deliver the messages from.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
        public static IServiceCollection AddPhemeDbContext<TContext>(
            this IServiceCollection services,
            Action<PhemeDbContextOptions> configure)
            where TContext : DbContext
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            DbContextRegistration<TContext> registration = DbContextRegistration<TContext>.GetOrAdd(services);
            configure(registration.Options);
            registration.AddServices(services);

            return services;
        }
    }
}
