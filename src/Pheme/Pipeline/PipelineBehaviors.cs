using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pheme.Internal;
using Pheme.Logging;

namespace Pheme.Pipeline
{
    /// <summary>
    /// The pipeline behaviors added by all <c>AddPheme</c> calls on one service collection, in pipeline order (outermost
    /// first), stored in the collection itself. Shared by every service provider built from the collection; a change
    /// increments <see cref="Version"/>, so a behavior added after a send also applies to later sends.
    /// </summary>
    internal sealed class PipelineBehaviors
    {
        private readonly IServiceCollection services;
        private readonly Lock gate = new();

        // Closed types and open generic type definitions, as added.
        private readonly List<Type> behaviorTypes = [];
        private bool requestLogging;
        private int version;

        private PipelineBehaviors(IServiceCollection services)
        {
            this.services = services;
        }

        /// <summary>
        /// Gets the number of changes so far, for caches of <see cref="Close"/>.
        /// </summary>
        public int Version => Volatile.Read(ref version);

        /// <exception cref="InvalidOperationException">The instance in <paramref name="services"/> was created for another collection.</exception>
        public static PipelineBehaviors GetOrAdd(IServiceCollection services)
        {
            if (CollectionState.Find<PipelineBehaviors>(services, behaviors => behaviors.services) is { } existing)
            {
                return existing;
            }

            var behaviors = new PipelineBehaviors(services);
            services.AddSingleton(behaviors);
            return behaviors;
        }

        /// <summary>
        /// Appends the behaviors not added yet and registers each as a transient service of its own type, unless the
        /// application has registered it already.
        /// </summary>
        public void Add(IEnumerable<Type> behaviors)
        {
            lock (gate)
            {
                foreach (Type behaviorType in behaviors)
                {
                    if (!behaviorTypes.Contains(behaviorType))
                    {
                        behaviorTypes.Add(behaviorType);
                        services.TryAdd(ServiceDescriptor.Transient(behaviorType, behaviorType));
                        Volatile.Write(ref version, version + 1);
                    }
                }
            }
        }

        /// <summary>
        /// Puts <see cref="RequestLoggingBehavior{TMessage, TResponse}"/> in front of all other behaviors, whenever they
        /// are added, and registers it as a singleton.
        /// </summary>
        public void AddRequestLogging()
        {
            lock (gate)
            {
                if (!requestLogging)
                {
                    requestLogging = true;
                    services.TryAdd(ServiceDescriptor.Singleton(typeof(RequestLoggingBehavior<,>), typeof(RequestLoggingBehavior<,>)));
                    Volatile.Write(ref version, version + 1);
                }
            }
        }

        /// <summary>
        /// Gets the closed behavior types that apply to <paramref name="messageType"/> with <paramref name="responseType"/>,
        /// in pipeline order, and the <see cref="Version"/> they were computed for.
        /// </summary>
        public (Type[] Types, int Version) Close(Type messageType, Type responseType)
        {
            Type serviceType = typeof(IPipelineBehavior<,>).MakeGenericType(messageType, responseType);
            lock (gate)
            {
                var closedTypes = new List<Type>();
                if (requestLogging)
                {
                    closedTypes.Add(typeof(RequestLoggingBehavior<,>).MakeGenericType(messageType, responseType));
                }

                foreach (Type behaviorType in behaviorTypes)
                {
                    if (BehaviorTypes.TryClose(behaviorType, serviceType, out Type? closedType))
                    {
                        closedTypes.Add(closedType);
                    }
                }

                return ([.. closedTypes], version);
            }
        }
    }
}
