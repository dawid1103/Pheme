using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pheme.Outbox;
using Pheme.Pipeline;
using Pheme.Registration;

namespace Pheme
{
    /// <summary>
    /// Configures one call of <see cref="PhemeServiceCollectionExtensions.AddPheme"/>: where handlers are scanned
    /// for, which pipeline behaviors wrap them, request logging, and the outbox delivering integration events.
    /// </summary>
    public sealed class PhemeOptions
    {
        private readonly List<Assembly> assemblies = [];
        private readonly List<Type> behaviors = [];
        private readonly List<Action<IServiceCollection, Assembly>> assemblyScanners = [];

        internal PhemeOptions(IServiceCollection services)
        {
            Services = services;
        }

        /// <summary>
        /// Gets the service collection this <c>AddPheme</c> call registers into, for extension packages.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public IServiceCollection Services { get; }

        internal IReadOnlyList<Assembly> Assemblies => assemblies;

        internal IReadOnlyList<Type> Behaviors => behaviors;

        internal IReadOnlyList<Action<IServiceCollection, Assembly>> AssemblyScanners => assemblyScanners;

        internal bool RequestLogging { get; private set; }

        /// <summary>
        /// Adds the command, query, domain event and integration event handlers defined in the given assemblies, including
        /// <see langword="internal"/> ones, as transient services.
        /// </summary>
        /// <param name="assemblies">The assemblies holding the handlers or the messages.</param>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>
        /// <para>
        /// The check at start-up reports a missing handler only for the messages of the added assemblies, and with the
        /// outbox (<see cref="AddOutbox()"/>) for their integration events. Add an assembly of messages or integration
        /// events without handlers too, such as the contracts shared by the modules of a modular monolith: otherwise a
        /// message of it without a handler fails only when it is sent, and an integration event when it is written.
        /// </para>
        /// <para>
        /// Handlers are transient, so they may depend on scoped services such as a <c>DbContext</c>. A handler with
        /// another lifetime is registered by hand before <c>AddPheme</c>, for example
        /// <c>services.AddScoped&lt;ICommandHandler&lt;PlaceOrder&gt;, PlaceOrderHandler&gt;()</c>: the scan skips a
        /// class already registered under that interface.
        /// </para>
        /// <para>
        /// Extension packages search the same assemblies for their own types, for example
        /// <c>AddFluentValidation</c> for validators, so keep such types in the assemblies of the handlers. Pass the
        /// assemblies explicitly: <c>AppDomain.CurrentDomain.GetAssemblies()</c> returns only the assemblies loaded so
        /// far. An assembly added again is added once.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="assemblies"/> or one of its elements is <see langword="null"/>.</exception>
        public PhemeOptions AddHandlersFromAssemblies(params Assembly[] assemblies)
        {
            ArgumentNullException.ThrowIfNull(assemblies);

            foreach (Assembly assembly in assemblies)
            {
                ArgumentNullException.ThrowIfNull(assembly, nameof(assemblies));

                if (!this.assemblies.Contains(assembly))
                {
                    this.assemblies.Add(assembly);
                }
            }

            return this;
        }

        /// <summary>
        /// Adds the command, query, domain event and integration event handlers defined in the assembly containing
        /// <typeparamref name="T"/>, including <see langword="internal"/> ones, as transient services.
        /// </summary>
        /// <typeparam name="T">
        /// Any type from the assembly holding the handlers or the messages, for example one of its messages.
        /// </typeparam>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>Works like <see cref="AddHandlersFromAssemblies(Assembly[])"/>.</remarks>
        public PhemeOptions AddHandlersFromAssemblyContaining<T>()
        {
            return AddHandlersFromAssemblies(typeof(T).Assembly);
        }

        /// <summary>
        /// Adds the command, query, domain event and integration event handlers defined in the assemblies containing the
        /// given types, including <see langword="internal"/> ones, as transient services.
        /// </summary>
        /// <param name="markerTypes">
        /// Any type from each assembly holding the handlers or the messages, for example a static module registration
        /// class, which <see cref="AddHandlersFromAssemblyContaining{T}"/> cannot take as its type argument.
        /// </param>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>Works like <see cref="AddHandlersFromAssemblies(Assembly[])"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="markerTypes"/> or one of its elements is <see langword="null"/>.</exception>
        public PhemeOptions AddHandlersFromAssemblyContaining(params Type[] markerTypes)
        {
            ArgumentNullException.ThrowIfNull(markerTypes);

            foreach (Type markerType in markerTypes)
            {
                ArgumentNullException.ThrowIfNull(markerType, nameof(markerTypes));
                AddHandlersFromAssemblies(markerType.Assembly);
            }

            return this;
        }

        /// <summary>
        /// Adds a pipeline behavior that wraps the handling of every message it applies to.
        /// </summary>
        /// <param name="behaviorType">
        /// A class implementing <see cref="IPipelineBehavior{TMessage, TResponse}"/>: an open generic type such as
        /// <c>typeof(AuditBehavior&lt;,&gt;)</c>, or a closed type for specific messages.
        /// </param>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>
        /// <para>
        /// Behaviors run in the order they are added, the first one outermost, across <c>AddPheme</c> calls in call
        /// order. A behavior added again keeps its first position.
        /// </para>
        /// <para>
        /// An open generic behavior is closed with the message type and its response type, <see cref="Unit"/> for an
        /// <see cref="ICommand"/>, and applies only to the messages that satisfy its generic constraints. A closed
        /// behavior applies to the messages its <see cref="IPipelineBehavior{TMessage, TResponse}"/> interface accepts.
        /// </para>
        /// <para>
        /// The behavior is registered as a transient service of its own type, unless that type is already registered;
        /// register it before <c>AddPheme</c> to choose another lifetime.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="behaviorType"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="behaviorType"/> is not a non-abstract class implementing
        /// <see cref="IPipelineBehavior{TMessage, TResponse}"/>, is partially closed, or is an open generic type with a
        /// type parameter that does not appear in that interface.
        /// </exception>
        public PhemeOptions AddBehavior(Type behaviorType)
        {
            ArgumentNullException.ThrowIfNull(behaviorType);
            BehaviorTypes.Validate(behaviorType, nameof(behaviorType));

            if (!behaviors.Contains(behaviorType))
            {
                behaviors.Add(behaviorType);
            }

            return this;
        }

        /// <summary>
        /// Adds a built-in behavior that logs how the handling of every message ended and how long it took, in the
        /// category <see cref="RequestLoggingOptions.CategoryName"/>, with the default options or the configuration of
        /// other calls.
        /// </summary>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>
        /// The outermost behavior, whatever the order of <see cref="AddBehavior(Type)"/> calls. Several calls add it once
        /// and configure the same <see cref="RequestLoggingOptions"/> in call order. Requires an
        /// <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/>, registered by every host.
        /// </remarks>
        public PhemeOptions AddRequestLogging()
        {
            Services.AddOptions<RequestLoggingOptions>();
            RequestLogging = true;
            return this;
        }

        /// <summary>
        /// Adds a built-in behavior that logs how the handling of every message ended and how long it took, in the
        /// category <see cref="RequestLoggingOptions.CategoryName"/>, and configures it.
        /// </summary>
        /// <param name="configure">Sets the level of each outcome and which exceptions are expected.</param>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>Works like <see cref="AddRequestLogging()"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public PhemeOptions AddRequestLogging(Action<RequestLoggingOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            Services.AddOptions<RequestLoggingOptions>().Configure(configure);
            RequestLogging = true;
            return this;
        }

        /// <summary>
        /// Adds the outbox with the default <see cref="OutboxOptions"/> or the configuration of other calls: the
        /// delivery of integration events to their <see cref="IIntegrationEventHandler{TEvent}"/> handlers by the outbox
        /// processor, a hosted service, and by <see cref="IOutboxRunner"/>, and the <see cref="IOutboxMessageFactory"/>
        /// of the outbox stores.
        /// </summary>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>
        /// <para>
        /// Required by integration event handlers: without the outbox nothing delivers their events, so handlers
        /// registered without it fail the start of the application. Integration events are not delivered in-process;
        /// events handled within the transaction of a change are domain events. The outbox in turn requires an
        /// <see cref="IOutboxStore"/>, for example <c>UseOutbox</c> of <c>AddPhemeDbContext</c> in
        /// <c>Pheme.EntityFrameworkCore</c>; without one the start fails as well.
        /// </para>
        /// <para>
        /// The processor runs while the host runs, in one instance of the application only: the stores do not lock
        /// the messages they deliver. It logs in the category <see cref="OutboxOptions.CategoryName"/> and requires an
        /// <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/>, registered by every host. It waits with the
        /// registered <see cref="TimeProvider"/>, <see cref="TimeProvider.System"/> unless the application registers
        /// another.
        /// </para>
        /// <para>
        /// The event types are found from the registered handlers, scanned or registered by hand; each is stored under
        /// the name of its <see cref="IntegrationEventNameAttribute"/>, otherwise its full name. An empty or duplicate
        /// name, a generic event type, a handler of an interface or abstract event type and a concrete event type of an
        /// assembly added with <see cref="AddHandlersFromAssemblies(Assembly[])"/> without a handler fail the start of
        /// the application; an event of another assembly whose runtime type has no handler fails when it is written to
        /// the outbox (<see cref="IOutboxWriter{TModule}.Add"/>). Several calls add the outbox once and configure the
        /// same <see cref="OutboxOptions"/> in call order.
        /// </para>
        /// </remarks>
        public PhemeOptions AddOutbox()
        {
            AddOutboxServices();
            return this;
        }

        /// <summary>
        /// Adds the outbox and configures it: the delivery of integration events to their
        /// <see cref="IIntegrationEventHandler{TEvent}"/> handlers and the <see cref="IOutboxMessageFactory"/> of the
        /// outbox stores.
        /// </summary>
        /// <param name="configure">
        /// Sets how the outbox processor polls the stores, how it retries a failed message and whether it runs at all.
        /// </param>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>Works like <see cref="AddOutbox()"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public PhemeOptions AddOutbox(Action<OutboxOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            AddOutboxServices().Configure(configure);
            return this;
        }

        // The dispatcher's registration also tells the startup validation that the outbox is added.
        private OptionsBuilder<OutboxOptions> AddOutboxServices()
        {
            Services.TryAddSingleton<IOutboxMessageFactory, OutboxMessageFactory>();

            // Built on first use from the complete collection, so handlers registered by hand after AddPheme count.
            Services.TryAddSingleton(serviceProvider => new IntegrationEventTypes(serviceProvider.GetRequiredService<HandlerRegistry>().Services));
            Services.TryAddSingleton<IntegrationEventDispatcher>();
            Services.TryAddSingleton<IOutboxRunner, OutboxRunner>();
            Services.TryAddScoped<IOutboxMaintenance, OutboxMaintenance>();
            Services.TryAddSingleton(TimeProvider.System);
            Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxProcessor>());
            return Services.AddOptions<OutboxOptions>();
        }

        /// <summary>
        /// Adds a scanner that runs once for every assembly added with
        /// <see cref="AddHandlersFromAssemblies(Assembly[])"/> in this service collection, for extension packages that
        /// register services found next to the handlers, such as validators.
        /// </summary>
        /// <param name="scanner">
        /// Registers the services found in the given assembly into the given service collection. Pass a static method;
        /// an equal delegate is added once.
        /// </param>
        /// <returns>These options, for chaining.</returns>
        /// <remarks>
        /// Covers the assemblies of every <c>AddPheme</c> call on the service collection, earlier and later ones included.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="scanner"/> is <see langword="null"/>.</exception>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public PhemeOptions AddAssemblyScanner(Action<IServiceCollection, Assembly> scanner)
        {
            ArgumentNullException.ThrowIfNull(scanner);

            if (!assemblyScanners.Contains(scanner))
            {
                assemblyScanners.Add(scanner);
            }

            return this;
        }
    }
}
