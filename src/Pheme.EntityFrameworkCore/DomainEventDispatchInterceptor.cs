using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pheme
{
    /// <summary>
    /// Publishes the domain events of the entities tracked by <typeparamref name="TContext"/> before it saves, so the
    /// changes made by their handlers are saved by the same <c>SaveChanges</c>.
    /// </summary>
    internal sealed class DomainEventDispatchInterceptor<TContext>(
        IDomainEventPublisher publisher,
        DbContextRegistration<TContext> registration)
        : SaveChangesInterceptor
        where TContext : DbContext
    {
        // The context whose save is publishing domain events now; the interceptor is scoped, one per scope and context
        // type, and another instance of the type may be saved by a handler.
        private DbContext? publishing;

        /// <summary>
        /// Resolves the interceptor from the scope the context is created in, with a readable error when the options
        /// of the context are not scoped or <c>AddPheme</c> is missing, instead of the container's error about an
        /// internal type.
        /// </summary>
        /// <exception cref="OptionsValidationException">The options of the context are not scoped.</exception>
        public static DomainEventDispatchInterceptor<TContext> Resolve(IServiceProvider serviceProvider)
        {
            // Without a host nothing has run the startup validation: options built outside a scope fail here, before
            // the container's error about resolving the scoped interceptor from the root provider.
            _ = serviceProvider.GetRequiredService<IOptions<DomainEventDispatchValidationOptions<TContext>>>().Value;

            if (serviceProvider.GetService(typeof(IDomainEventPublisher)) is null)
            {
                throw new InvalidOperationException(
                    $"{typeof(TContext).Name} publishes domain events through Pheme, which is not registered; call "
                    + "services.AddPheme(...) in the composition root.");
            }

            return (DomainEventDispatchInterceptor<TContext>)serviceProvider.GetService(typeof(DomainEventDispatchInterceptor<TContext>))!;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            EnsureNotPublishing(eventData.Context);

            // Handlers are asynchronous; blocking on them here could deadlock, so a synchronous save is allowed only
            // when there is nothing to publish.
            if (eventData.Context is { } context && CollectPending(context).Count > 0)
            {
                throw new InvalidOperationException(
                    $"The entities tracked by {context.GetType().Name} have domain events to publish, and their handlers "
                    + "run asynchronously; call SaveChangesAsync instead of SaveChanges.");
            }

            return result;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            EnsureNotPublishing(eventData.Context);

            if (eventData.Context is not { } context)
            {
                return result;
            }

            DbContext? outer = publishing;
            publishing = context;
            try
            {
                int maxRounds = registration.Options.MaxDomainEventRounds;
                for (int round = 0; ; round++)
                {
                    List<PendingEntity> pending = CollectPending(context);
                    if (pending.Count == 0)
                    {
                        return result;
                    }

                    if (round == maxRounds)
                    {
                        throw new InvalidOperationException(
                            $"Domain event handlers still raised events after {maxRounds} rounds; they probably raise each "
                            + $"other's events in a cycle. The limit is {nameof(PhemeDbContextOptions)}.{nameof(PhemeDbContextOptions.MaxDomainEventRounds)}.");
                    }

                    object[] domainEvents = [.. pending.SelectMany(entity => entity.DomainEvents)];
                    foreach (PendingEntity entity in pending)
                    {
                        entity.Collector.ClearDomainEvents(entity.Entity);
                    }

                    await publisher.Publish(domainEvents, cancellationToken);
                }
            }
            finally
            {
                publishing = outer;
            }
        }

        /// <summary>
        /// Fails a save of the context from a handler of the domain events its save publishes: EF Core would save the
        /// changes so far in a transaction of their own, which an exception of a later handler no longer rolls back.
        /// </summary>
        private void EnsureNotPublishing(DbContext? context)
        {
            if (context is not null && ReferenceEquals(context, publishing))
            {
                throw new InvalidOperationException(
                    $"A domain event handler saved {context.GetType().Name} while it was publishing domain events. The save "
                    + "that publishes an event also saves the changes of its handlers, in one transaction; change the "
                    + "context in the handler without saving it, also through a command sent from the handler.");
            }
        }

        private List<PendingEntity> CollectPending(DbContext context)
        {
            EnsureCollectorsMatchModel(context);

            var pending = new List<PendingEntity>();
            var collectorsByType = new Dictionary<Type, DomainEventCollector?>();
            foreach (EntityEntry entry in context.ChangeTracker.Entries())
            {
                object entity = entry.Entity;
                Type entityType = entity.GetType();
                if (!collectorsByType.TryGetValue(entityType, out DomainEventCollector? collector))
                {
                    collector = FindCollector(entityType);
                    collectorsByType.Add(entityType, collector);
                }

                if (collector is null)
                {
                    continue;
                }

                IEnumerable<object> domainEvents = collector.GetDomainEvents(entity)
                    ?? throw new InvalidOperationException(
                        $"The domain events of entity {entityType.FullName}, collected as {collector.EntityType.FullName}, "
                        + "are null; return an empty collection when there are none.");
                object[] copy = [.. domainEvents];
                if (copy.Length > 0)
                {
                    pending.Add(new PendingEntity(entity, collector, copy));
                }
            }

            return pending;
        }

        /// <summary>
        /// Fails a save while a collector matches no entity type of the model: the context would silently publish none
        /// of its events, for example of an interface of another module. Not checked again once it passes for the
        /// registration of the context.
        /// </summary>
        private void EnsureCollectorsMatchModel(DbContext context)
        {
            if (registration.CollectorsMatchModel)
            {
                return;
            }

            Type[] entityTypes = [.. context.Model.GetEntityTypes().Select(entityType => entityType.ClrType)];
            DomainEventCollector? unmatched = registration.Options.Collectors
                .FirstOrDefault(collector => !entityTypes.Any(collector.EntityType.IsAssignableFrom));
            if (unmatched is not null)
            {
                throw new InvalidOperationException(
                    $"{context.GetType().Name} collects domain events from {unmatched.EntityType.FullName} "
                    + $"({nameof(PhemeDbContextOptions.CollectDomainEventsFrom)} in AddPhemeDbContext), but no entity type of "
                    + "its model is assignable to it, so it would collect none; collect from a type its entities implement "
                    + "or derive from, or register the collector for the context that maps them.");
            }

            registration.CollectorsMatchModel = true;
        }

        private DomainEventCollector? FindCollector(Type entityType)
        {
            DomainEventCollector? found = null;
            foreach (DomainEventCollector collector in registration.Options.Collectors)
            {
                if (!collector.EntityType.IsAssignableFrom(entityType))
                {
                    continue;
                }

                if (found is not null)
                {
                    throw new InvalidOperationException(
                        $"Entity {entityType.FullName} matches both {found.EntityType.FullName} and "
                        + $"{collector.EntityType.FullName} registered with "
                        + $"{nameof(PhemeDbContextOptions.CollectDomainEventsFrom)}, so its events would be published "
                        + "twice; register only one of them for this entity.");
                }

                found = collector;
            }

            return found;
        }

        private sealed record PendingEntity(object Entity, DomainEventCollector Collector, object[] DomainEvents);
    }
}
