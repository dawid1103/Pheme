using Microsoft.Extensions.DependencyInjection.Extensions;
using Pheme;
using Pheme.DomainEvents;
using Pheme.Pipeline;
using Pheme.Registration;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers Pheme in an <see cref="IServiceCollection"/>.
    /// </summary>
    public static class PhemeServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="ISender"/>, <see cref="IDomainEventPublisher"/>, the command, query, domain event and
        /// integration event handlers found in the assemblies given in <paramref name="configure"/>, and the pipeline
        /// behaviors and the outbox added there.
        /// </summary>
        /// <param name="services">The service collection to add to.</param>
        /// <param name="configure">Selects the assemblies to scan, the behaviors and the outbox.</param>
        /// <returns>The same service collection, for chaining.</returns>
        /// <remarks>
        /// <para>
        /// May be called once per module, from its infrastructure with its assemblies, and once in the composition root
        /// with the behaviors and the outbox: each assembly is scanned once, and the behaviors of all calls form one
        /// pipeline in call order. <see cref="ISender"/> and <see cref="IDomainEventPublisher"/> are scoped; code running
        /// outside a request creates a scope. The registrations are validated on host start, or else on the first
        /// resolution of <see cref="ISender"/>, with all problems in one
        /// <see cref="Microsoft.Extensions.Options.OptionsValidationException"/>.
        /// </para>
        /// <para>
        /// Domain event and integration event handlers found by the scan run in the order of their full type names, after
        /// handlers registered by hand before <c>AddPheme</c>.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// An assembly cannot be scanned because some of its types cannot be loaded, or <paramref name="services"/>
        /// contains Pheme registrations copied from another service collection.
        /// </exception>
        public static IServiceCollection AddPheme(this IServiceCollection services, Action<PhemeOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            var options = new PhemeOptions(services);
            configure(options);

            HandlerRegistry registry = HandlerRegistry.GetOrAdd(services);
            services.AddPhemeCore();
            services.TryAddScoped<IDomainEventPublisher, DomainEventPublisher>();
            registry.Register(options);
            PipelineBehaviors behaviors = PipelineBehaviors.GetOrAdd(services);
            behaviors.Add(options.Behaviors);
            if (options.RequestLogging)
            {
                behaviors.AddRequestLogging();
            }

            return services;
        }
    }
}
