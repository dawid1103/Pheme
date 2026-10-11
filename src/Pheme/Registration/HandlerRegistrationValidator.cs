using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pheme.Internal;
using Pheme.Outbox;

namespace Pheme.Registration
{
    /// <summary>
    /// Checks the registrations of all <c>AddPheme</c> calls once the service collection is complete, and reports every
    /// problem in one failure.
    /// </summary>
    internal sealed class HandlerRegistrationValidator(HandlerRegistry registry) : IValidateOptions<HandlerRegistrationOptions>
    {
        public ValidateOptionsResult Validate(string? name, HandlerRegistrationOptions options)
        {
            IReadOnlyList<string> problems = FindProblems(
                registry.MessageTypes, registry.IntegrationEventTypes, registry.Services, registry.Assemblies);
            if (problems.Count == 0)
            {
                return ValidateOptionsResult.Success;
            }

            string header = $"Pheme handler registrations have {problems.Count} {(problems.Count == 1 ? "problem" : "problems")}:";
            return ValidateOptionsResult.Fail(string.Join(Environment.NewLine + "- ", [header, .. problems]));
        }

        /// <summary>
        /// Finds the messages that cannot be sent and the registrations Pheme would silently ignore: messages without a
        /// handler or without exactly one message interface among <paramref name="messageTypes"/>, messages with more
        /// than one handler, pipeline behaviors registered as <see cref="IPipelineBehavior{TMessage, TResponse}"/>
        /// services among all <paramref name="services"/>, domain event handlers of an interface or abstract event type,
        /// and the integration events and handlers the outbox would not deliver
        /// (<see cref="IntegrationEventTypes.FindProblems"/>).
        /// </summary>
        /// <param name="messageTypes">The messages that must have a handler.</param>
        /// <param name="integrationEventTypes">
        /// The integration events that must have a handler when the outbox is added.
        /// </param>
        /// <param name="services">All registrations, including the ones made by hand.</param>
        /// <param name="assemblies">The scanned assemblies, named in the hint for a missing handler.</param>
        public static IReadOnlyList<string> FindProblems(
            IEnumerable<Type> messageTypes,
            IEnumerable<Type> integrationEventTypes,
            IEnumerable<ServiceDescriptor> services,
            IReadOnlyCollection<Assembly> assemblies)
        {
            ServiceDescriptor[] descriptors = [.. services];

            // Sender resolves handlers without a key, so keyed registrations neither satisfy nor duplicate a handler.
            ILookup<Type, ServiceDescriptor> handlers = descriptors
                .Where(descriptor => !descriptor.IsKeyedService && MessageContracts.IsHandlerInterface(descriptor.ServiceType))
                .ToLookup(descriptor => descriptor.ServiceType);
            var problems = new List<string>();

            foreach (Type messageType in messageTypes)
            {
                Type[] messageInterfaces = [.. messageType.GetInterfaces().Where(MessageContracts.IsMessageInterface)];
                if (messageInterfaces.Length == 0)
                {
                    problems.Add(
                        $"Message '{TypeNames.Format(messageType)}' implements only IMessage. "
                        + "Implement ICommand, ICommand<TResponse> or IQuery<TResponse>, not IMessage.");
                    continue;
                }

                if (messageInterfaces.Length > 1)
                {
                    problems.Add(
                        $"Message '{TypeNames.Format(messageType)}' implements "
                        + $"{string.Join(", ", messageInterfaces.Select(type => $"'{TypeNames.Format(type)}'"))}. "
                        + "A message implements exactly one of ICommand, ICommand<TResponse> and IQuery<TResponse>.");
                    continue;
                }

                Type handlerInterface = MessageContracts.GetHandlerInterface(messageType, messageInterfaces[0]);
                if (!handlers.Contains(handlerInterface))
                {
                    problems.Add(
                        $"{Capitalize(MessageContracts.GetMessageKind(handlerInterface))} '{TypeNames.Format(messageType)}' has no handler. "
                        + $"Implement '{TypeNames.Format(handlerInterface)}'. {SearchHint(assemblies)}");
                }
            }

            foreach (IGrouping<Type, ServiceDescriptor> registrations in handlers.Where(registrations => registrations.Skip(1).Any()))
            {
                string message = $"{Capitalize(MessageContracts.GetMessageKind(registrations.Key))} "
                    + $"'{TypeNames.Format(registrations.Key.GetGenericArguments()[0])}'";
                Type?[] handlerTypes = [.. registrations.Select(GetImplementationType).Distinct()];
                if (handlerTypes is [{ } handlerType])
                {
                    // One class registered several times is one handler registered twice, not two handlers.
                    problems.Add(
                        $"{message} has handler '{TypeNames.Format(handlerType)}' registered {registrations.Count()} times. "
                        + (assemblies.Contains(handlerType.Assembly)
                            ? "AddPheme already registers the handlers of its assembly, so a registration by hand is a second "
                                + "one; remove it, or keep it before AddPheme to give the handler another lifetime."
                            : "Remove the extra registrations."));
                    continue;
                }

                problems.Add(
                    $"{message} has {registrations.Count()} handlers: {string.Join(", ", registrations.Select(Describe))}. "
                    + "A message has exactly one handler; remove the others. To swap a handler, for example for a fake in "
                    + "tests, replace its registration with services.Replace(...), or remove it with "
                    + $"services.RemoveAll<{TypeNames.Format(registrations.Key)}>() before adding the other one.");
            }

            // Pheme resolves behaviors by their own type, never by this interface.
            foreach (ServiceDescriptor descriptor in descriptors.Where(IsBehaviorService))
            {
                string behavior = GetImplementationType(descriptor) is { } behaviorType ? TypeOf(behaviorType) : "typeof(MyBehavior<,>)";
                problems.Add(
                    $"Service '{TypeNames.Format(descriptor.ServiceType)}' is registered with {Describe(descriptor)}, but Pheme "
                    + $"does not resolve pipeline behaviors as this service. Add behaviors with PhemeOptions.AddBehavior({behavior}).");
            }

            // The publisher resolves the handlers of an event's exact runtime type without a key, and a runtime type is
            // never abstract.
            foreach (IGrouping<Type, ServiceDescriptor> registrations in descriptors
                .Where(descriptor => !descriptor.IsKeyedService && MessageContracts.IsDomainEventHandlerInterface(descriptor.ServiceType))
                .GroupBy(descriptor => descriptor.ServiceType.GetGenericArguments()[0])
                .Where(registrations => registrations.Key.IsAbstract))
            {
                string eventType = TypeNames.Format(registrations.Key);
                problems.Add(
                    $"Domain event '{eventType}' is {(registrations.Key.IsInterface ? "an interface" : "abstract")}, so its "
                    + $"handlers {string.Join(", ", registrations.Select(Describe).Distinct())} never run: Pheme publishes an "
                    + "event only to the handlers of its exact runtime type. Handle the concrete event types, or all of them "
                    + "with an open generic handler, MyDomainEventHandler<TEvent> : IDomainEventHandler<TEvent> where TEvent : "
                    + $"{eventType}, registered by hand with "
                    + "services.AddTransient(typeof(IDomainEventHandler<>), typeof(MyDomainEventHandler<>)).");
            }

            problems.AddRange(IntegrationEventTypes.FindProblems(descriptors, integrationEventTypes, assemblies));
            return problems;
        }

        /// <summary>
        /// Names the scanned assemblies in the hint for a missing handler.
        /// </summary>
        public static string SearchHint(IEnumerable<Assembly> assemblies)
        {
            return $"Pheme searched assemblies {string.Join(", ", assemblies.Select(assembly => $"'{assembly.GetName().Name}'"))}; "
                + "is the handler's assembly added with AddHandlersFromAssemblies?";
        }

        private static bool IsBehaviorService(ServiceDescriptor descriptor)
        {
            return descriptor.ServiceType.IsGenericType && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>);
        }

        /// <summary>
        /// Describes the implementation of a registration for an error message: its type, its instance's type or a
        /// factory delegate.
        /// </summary>
        public static string Describe(ServiceDescriptor descriptor)
        {
            object? instance = descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance;
            return GetImplementationType(descriptor) is { } implementationType
                ? $"'{TypeNames.Format(implementationType)}'"
                : instance is not null
                    ? $"'{TypeNames.Format(instance.GetType())}' (instance)"
                    : "a factory delegate";
        }

        private static Type? GetImplementationType(ServiceDescriptor descriptor)
        {
            return descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;
        }

        /// <summary>
        /// Formats <paramref name="type"/> as a C# <c>typeof</c> expression, such as <c>typeof(Ns.MyBehavior&lt;,&gt;)</c>.
        /// </summary>
        private static string TypeOf(Type type)
        {
            if (!type.IsGenericTypeDefinition)
            {
                return $"typeof({TypeNames.Format(type)})";
            }

            string name = TypeNames.Format(type);
            return $"typeof({name[..name.IndexOf('<', StringComparison.Ordinal)]}<{new string(',', type.GetGenericArguments().Length - 1)}>)";
        }

        private static string Capitalize(string word)
        {
            return string.Concat(word[..1].ToUpperInvariant(), word[1..]);
        }
    }
}
