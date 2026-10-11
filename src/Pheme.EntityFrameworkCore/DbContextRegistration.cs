using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pheme
{
    /// <summary>
    /// The options of one context, as the checks across all contexts with an outbox see them.
    /// </summary>
    internal abstract class DbContextRegistration
    {
        private const string ContextSuffix = "DbContext";

        private readonly string defaultOutboxSource;

        protected DbContextRegistration(Type contextType)
        {
            ContextType = contextType;
            defaultOutboxSource = contextType.Name.Length > ContextSuffix.Length && contextType.Name.EndsWith(ContextSuffix, StringComparison.Ordinal)
                ? contextType.Name[..^ContextSuffix.Length]
                : contextType.Name;
        }

        public Type ContextType { get; }

        public PhemeDbContextOptions Options { get; } = new();

        /// <summary>
        /// Gets the <see cref="IOutboxStore.Source"/> of the context's outbox: <see cref="PhemeDbContextOptions.OutboxSource"/>,
        /// otherwise the name of the context type without the suffix <c>DbContext</c>.
        /// </summary>
        public string OutboxSource => Options.OutboxSource ?? defaultOutboxSource;
    }

    /// <summary>
    /// Everything Pheme does with <typeparamref name="TContext"/>: the options of all its <c>AddPhemeDbContext</c>
    /// calls, stored in the service collection as a singleton instance, and the services they need.
    /// </summary>
    /// <remarks>
    /// The options are configured when <c>AddPhemeDbContext</c> is called, not when they are first read, because
    /// <see cref="PhemeDbContextOptions.UseOutbox{TModule}"/> decides which services to register.
    /// </remarks>
    internal sealed class DbContextRegistration<TContext> : DbContextRegistration
        where TContext : DbContext
    {
        private bool dispatchAdded;
        private int outboxWriters;

        private DbContextRegistration()
            : base(typeof(TContext))
        {
        }

        /// <summary>
        /// Gets or sets whether each collector of the options matches an entity type of the model of the context,
        /// checked by the dispatch interceptor on each save until it passes.
        /// </summary>
        public bool CollectorsMatchModel { get; set; }

        public static DbContextRegistration<TContext> GetOrAdd(IServiceCollection services)
        {
            if (services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(DbContextRegistration<TContext>))?.ImplementationInstance
                is DbContextRegistration<TContext> existing)
            {
                return existing;
            }

            var registration = new DbContextRegistration<TContext>();
            services.AddSingleton(registration);
            return registration;
        }

        /// <summary>
        /// Gets the outbox messages of <paramref name="context"/>, of all sources.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The model of the context does not map the outbox, or its database is not supported.
        /// </exception>
        public static DbSet<OutboxMessage> OutboxMessages(TContext context)
        {
            if (context.Model.FindEntityType(typeof(OutboxMessage)) is null)
            {
                throw new InvalidOperationException(
                    $"{typeof(TContext).Name} writes integration events to its outbox, but its model has no outbox table; "
                    + "call modelBuilder.AddPhemeOutbox() in its OnModelCreating.");
            }

            OutboxDatabases.EnsureSupported(context, typeof(TContext));
            return context.Set<OutboxMessage>();
        }

        /// <summary>
        /// Registers the services that the options need and the earlier calls have not registered yet.
        /// </summary>
        public void AddServices(IServiceCollection services)
        {
            // A context that collects no domain events needs no interceptor, and no AddPheme for one.
            if (!dispatchAdded && Options.Collectors.Count > 0)
            {
                services.TryAddScoped<DomainEventDispatchInterceptor<TContext>>();
                services.AddSingleton<IValidateOptions<DomainEventDispatchValidationOptions<TContext>>>(
                    new DomainEventDispatchValidation<TContext>(services));
                services.AddOptions<DomainEventDispatchValidationOptions<TContext>>().ValidateOnStart();
                services.ConfigureDbContext<TContext>((serviceProvider, options) =>
                    options.AddInterceptors(DomainEventDispatchInterceptor<TContext>.Resolve(serviceProvider)));
                dispatchAdded = true;
            }

            if (outboxWriters == 0 && Options.OutboxModules.Count > 0)
            {
                // The first context with an outbox adds the check of all of them.
                if (!services.Any(descriptor => descriptor.ServiceType == typeof(DbContextRegistration)))
                {
                    services.AddSingleton<IValidateOptions<OutboxValidationOptions>, OutboxValidation>();
                    services.AddOptions<OutboxValidationOptions>().ValidateOnStart();
                }

                services.AddSingleton<DbContextRegistration>(this);
                services.AddScoped<IOutboxStore, EfOutboxStore<TContext>>();
            }

            // The modules only ever grow, without duplicates, so the ones after the count are new.
            foreach (Type module in Options.OutboxModules.Skip(outboxWriters))
            {
                services.AddScoped(
                    typeof(IOutboxWriter<>).MakeGenericType(module),
                    typeof(EfOutboxWriter<,>).MakeGenericType(typeof(TContext), module));
            }

            outboxWriters = Options.OutboxModules.Count;
        }
    }
}
