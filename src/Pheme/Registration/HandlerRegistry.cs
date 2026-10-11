using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pheme.Internal;

namespace Pheme.Registration
{
    /// <summary>
    /// The assemblies, messages and assembly scanners of all <c>AddPheme</c> calls on one service collection, stored in
    /// the collection itself.
    /// </summary>
    internal sealed class HandlerRegistry
    {
        private const int ReportedLoaderErrors = 3;

        private readonly List<Assembly> assemblies = [];
        private readonly List<Type> messageTypes = [];
        private readonly List<Type> integrationEventTypes = [];
        private readonly List<Action<IServiceCollection, Assembly>> scanners = [];

        private HandlerRegistry(IServiceCollection services)
        {
            Services = services;
        }

        /// <summary>
        /// Gets the service collection the handlers are registered in, read by the validation once it is complete.
        /// </summary>
        public IServiceCollection Services { get; }

        /// <summary>
        /// Gets the scanned assemblies, in the order they were registered.
        /// </summary>
        public IReadOnlyList<Assembly> Assemblies => assemblies;

        /// <summary>
        /// Gets the concrete message types defined in the scanned assemblies.
        /// </summary>
        public IReadOnlyList<Type> MessageTypes => messageTypes;

        /// <summary>
        /// Gets the concrete integration event types defined in the scanned assemblies.
        /// </summary>
        public IReadOnlyList<Type> IntegrationEventTypes => integrationEventTypes;

        /// <exception cref="InvalidOperationException">The instance in <paramref name="services"/> was created for another collection.</exception>
        public static HandlerRegistry GetOrAdd(IServiceCollection services)
        {
            if (CollectionState.Find<HandlerRegistry>(services, registry => registry.Services) is { } existing)
            {
                return existing;
            }

            var registry = new HandlerRegistry(services);
            services.AddSingleton(registry);
            services.AddOptions<HandlerRegistrationOptions>().ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<HandlerRegistrationOptions>, HandlerRegistrationValidator>());
            return registry;
        }

        public void Register(PhemeOptions options)
        {
            // A new scanner covers the assemblies of earlier calls now, and the new assemblies below with the others.
            foreach (Action<IServiceCollection, Assembly> scanner in options.AssemblyScanners)
            {
                if (!scanners.Contains(scanner))
                {
                    scanners.Add(scanner);
                    foreach (Assembly assembly in assemblies)
                    {
                        scanner(Services, assembly);
                    }
                }
            }

            foreach (Assembly assembly in options.Assemblies)
            {
                if (!assemblies.Contains(assembly))
                {
                    assemblies.Add(assembly);
                    Scan(assembly);
                    foreach (Action<IServiceCollection, Assembly> scanner in scanners)
                    {
                        scanner(Services, assembly);
                    }
                }
            }
        }

        private void Scan(Assembly assembly)
        {
            // Sorted, because GetTypes has no documented order and the registration order of event handlers is the
            // order they run in.
            foreach (Type type in GetTypes(assembly).OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                // Open generic types cannot be closed for unknown type arguments, so only concrete closed types count.
                if (type.IsAbstract || type.ContainsGenericParameters)
                {
                    continue;
                }

                if (typeof(IMessage).IsAssignableFrom(type))
                {
                    messageTypes.Add(type);
                }

                if (typeof(IIntegrationEvent).IsAssignableFrom(type))
                {
                    integrationEventTypes.Add(type);
                }

                if (!type.IsClass)
                {
                    continue;
                }

                foreach (Type serviceType in type.GetInterfaces())
                {
                    if (MessageContracts.IsHandlerInterface(serviceType)
                        || MessageContracts.IsDomainEventHandlerInterface(serviceType)
                        || MessageContracts.IsIntegrationEventHandlerInterface(serviceType))
                    {
                        // Transient, so a handler may depend on scoped services. TryAddEnumerable skips a handler already
                        // registered under this interface, also by hand - the way to give it another lifetime.
                        Services.TryAddEnumerable(ServiceDescriptor.Transient(serviceType, type));
                    }
                }
            }
        }

        private static Type[] GetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                IEnumerable<string> reasons = exception.LoaderExceptions
                    .Select(loaderException => loaderException?.Message)
                    .OfType<string>()
                    .Distinct()
                    .Take(ReportedLoaderErrors);
                throw new InvalidOperationException(
                    $"Pheme cannot scan assembly '{assembly.GetName().Name}' because some of its types cannot be loaded: "
                    + $"{string.Join(" ", reasons)} Is a dependency of the assembly missing or of another version?",
                    exception);
            }
        }
    }
}
