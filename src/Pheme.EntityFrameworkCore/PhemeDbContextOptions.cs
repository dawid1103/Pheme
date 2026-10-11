namespace Pheme
{
    /// <summary>
    /// Configures what Pheme does with one <c>DbContext</c>: which tracked entities its domain events are collected
    /// from and how they are published when it saves, and which modules write integration events to its outbox.
    /// </summary>
    /// <remarks>
    /// Each <c>DbContext</c> has its own options, configured by <c>AddPhemeDbContext&lt;TContext&gt;</c>. Several calls
    /// for the same context add up: the entity types and modules of all of them count, and the last value of
    /// <see cref="MaxDomainEventRounds"/> and <see cref="OutboxSource"/> wins.
    /// </remarks>
    public sealed class PhemeDbContextOptions
    {
        private readonly List<DomainEventCollector> collectors = [];
        private readonly List<Type> outboxModules = [];
        private int maxDomainEventRounds = 10;
        private string? outboxSource;

        /// <summary>
        /// Gets or sets how many rounds of domain event publication one save may run: handlers may raise further
        /// events, which are published in the next round. The default is 10.
        /// </summary>
        /// <remarks>
        /// A save that still has events to publish after this many rounds fails with an
        /// <see cref="InvalidOperationException"/>, since handlers raising each other's events would never end.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
        public int MaxDomainEventRounds
        {
            get => maxDomainEventRounds;
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
                maxDomainEventRounds = value;
            }
        }

        /// <summary>
        /// Gets or sets the name of the context's outbox, stored as <see cref="OutboxMessage.Source"/> of the messages it
        /// writes; <see langword="null"/>, the default, stands for the name of the context type without the suffix
        /// <c>DbContext</c>, for example <c>Ordering</c> for <c>OrderingDbContext</c>.
        /// </summary>
        /// <remarks>
        /// Each context with an outbox needs a name of its own: two contexts with the same one fail the start of the
        /// application. Set one when two contexts have the same type name in different namespaces, or to keep the
        /// source of the pending messages when the context type is renamed. The store of the context reads and updates only the
        /// messages of its source.
        /// </remarks>
        /// <exception cref="ArgumentException">The value is empty or white space.</exception>
        public string? OutboxSource
        {
            get => outboxSource;
            set
            {
                if (value is not null)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(value);
                }

                outboxSource = value;
            }
        }

        internal IReadOnlyList<DomainEventCollector> Collectors => collectors;

        internal IReadOnlyList<Type> OutboxModules => outboxModules;

        /// <summary>
        /// Collects domain events from the tracked entities of type <typeparamref name="TEntity"/>.
        /// </summary>
        /// <typeparam name="TEntity">
        /// The application's contract of entities raising events, usually an interface or a base class. Each tracked
        /// entity may match only one registered type.
        /// </typeparam>
        /// <param name="getDomainEvents">Returns the events of an entity not published yet, in the order to publish them.</param>
        /// <param name="clearDomainEvents">Forgets the events of an entity; called right before they are published.</param>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>
        /// Registering the same <typeparamref name="TEntity"/> again for the same context replaces its delegates. When no
        /// entity type of the context's model is assignable to <typeparamref name="TEntity"/>, for example an interface
        /// of another module's entities, each save of the context throws an <see cref="InvalidOperationException"/>
        /// instead of silently collecting nothing.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="getDomainEvents"/> or <paramref name="clearDomainEvents"/> is <see langword="null"/>.
        /// </exception>
        public PhemeDbContextOptions CollectDomainEventsFrom<TEntity>(
            Func<TEntity, IEnumerable<object>> getDomainEvents,
            Action<TEntity> clearDomainEvents)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(getDomainEvents);
            ArgumentNullException.ThrowIfNull(clearDomainEvents);

            var collector = new DomainEventCollector(
                typeof(TEntity),
                entity => getDomainEvents((TEntity)entity),
                entity => clearDomainEvents((TEntity)entity));
            int index = collectors.FindIndex(existing => existing.EntityType == typeof(TEntity));
            if (index >= 0)
            {
                collectors[index] = collector;
            }
            else
            {
                collectors.Add(collector);
            }

            return this;
        }

        /// <summary>
        /// Lets the module <typeparamref name="TModule"/> write integration events to the outbox table of this context:
        /// <c>AddPhemeDbContext</c> registers a scoped <see cref="IOutboxWriter{TModule}"/> that adds each event as an
        /// <see cref="OutboxMessage"/> to the context of its scope, saved by the context's next save in the same
        /// transaction as the other changes, and the context's <see cref="IOutboxStore"/>, from which the outbox of
        /// Pheme delivers them.
        /// </summary>
        /// <typeparam name="TModule">
        /// The marker type of the module, owned by its application layer, for example
        /// <c>public sealed class OrderingModule;</c>. An application without modules may use any type of its own, such
        /// as the context itself.
        /// </typeparam>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>
        /// <para>
        /// Several modules may share one context, each with its own call; the context has one store, whatever the
        /// number of modules. The same <typeparamref name="TModule"/> again for the same context is registered once,
        /// for two contexts it fails the start of the application. A writer writes only to its own context: a change
        /// in another context is saved in another transaction, so its events belong to that context's outbox.
        /// </para>
        /// <para>
        /// The model of the context must map the outbox with <c>modelBuilder.AddPhemeOutbox()</c> in
        /// <c>OnModelCreating</c>, and the context must use a database the outbox supports, PostgreSQL; otherwise
        /// <see cref="IOutboxWriter{TModule}.Add"/> and the store throw an <see cref="InvalidOperationException"/>. The messages are created and delivered by the outbox of Pheme,
        /// <c>AddOutbox</c> of <c>PhemeOptions</c>; without it the start of the application fails. Without a host,
        /// which validates on start, this error and those of a module or source in two contexts are thrown when a
        /// writer or the store is first resolved.
        /// </para>
        /// </remarks>
        public PhemeDbContextOptions UseOutbox<TModule>()
        {
            if (!outboxModules.Contains(typeof(TModule)))
            {
                outboxModules.Add(typeof(TModule));
            }

            return this;
        }
    }
}
