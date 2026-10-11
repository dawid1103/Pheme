using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Pheme.Internal;
using Pheme.Registration;

namespace Pheme.Outbox
{
    /// <summary>
    /// Maps the names stored in outbox messages to the integration event types that have handlers. Built from the
    /// registered <see cref="IIntegrationEventHandler{TEvent}"/> services, scanned and registered by hand, so event types
    /// need no registration of their own: only events with a handler are ever deserialized.
    /// </summary>
    internal sealed class IntegrationEventTypes
    {
        private readonly Dictionary<string, Type> types = new(StringComparer.Ordinal);

        /// <param name="services">All registrations, read once the service collection is complete.</param>
        public IntegrationEventTypes(IEnumerable<ServiceDescriptor> services)
        {
            // The startup validation reports the types left out and duplicate names; for a duplicate the first type wins.
            foreach (Type eventType in GetHandledTypes(services))
            {
                if (!eventType.IsAbstract && IntegrationEventNames.FindProblem(eventType) is null)
                {
                    types.TryAdd(IntegrationEventNames.Get(eventType), eventType);
                }
            }
        }

        /// <summary>
        /// Gets the event type stored under <paramref name="name"/>, or <see langword="null"/> when no handled event
        /// type has that name.
        /// </summary>
        public Type? Find(string name)
        {
            return types.GetValueOrDefault(name);
        }

        /// <summary>
        /// Finds the outbox configurations that would deliver nothing: the outbox without a store, integration events
        /// the outbox would park for want of a handler, integration event handlers registered without the outbox,
        /// handlers of an interface or abstract event type, and handlers of event types without a valid, unique stored
        /// name.
        /// </summary>
        /// <param name="services">All registrations, including the ones made by hand.</param>
        /// <param name="eventTypes">The integration events that must have a handler when the outbox is added.</param>
        /// <param name="assemblies">The scanned assemblies, named in the hint for a missing handler.</param>
        public static IReadOnlyList<string> FindProblems(
            IEnumerable<ServiceDescriptor> services,
            IEnumerable<Type> eventTypes,
            IReadOnlyCollection<Assembly> assemblies)
        {
            var problems = new List<string>();
            ServiceDescriptor[] descriptors = [.. services];
            bool outbox = descriptors.Any(descriptor => descriptor.ServiceType == typeof(IntegrationEventDispatcher));

            // The runner resolves stores without a key.
            if (outbox && !descriptors.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IOutboxStore)))
            {
                problems.Add(
                    "The outbox is added (PhemeOptions.AddOutbox), but no outbox store is registered, so the outbox "
                    + "processor would deliver nothing. Add the store of each DbContext that writes integration events "
                    + "with UseOutbox<TModule>() in AddPhemeDbContext<TContext> (Pheme.EntityFrameworkCore).");
            }

            // Without the outbox no event can be written (UseOutbox needs AddOutbox), so an event without a handler is
            // harmless until the outbox is added.
            if (outbox)
            {
                HashSet<Type> handled = [.. GetHandledTypes(descriptors)];
                foreach (Type eventType in eventTypes.Where(eventType => !handled.Contains(eventType)))
                {
                    problems.Add(
                        $"Integration event '{TypeNames.Format(eventType)}' has no handler, so the outbox would park it. "
                        + $"Implement 'IIntegrationEventHandler<{TypeNames.Format(eventType)}>'. "
                        + HandlerRegistrationValidator.SearchHint(assemblies));
                }
            }

            ServiceDescriptor[] handlers = [.. descriptors.Where(IsHandlerRegistration)];
            if (handlers.Length == 0)
            {
                return problems;
            }

            if (!outbox)
            {
                problems.Add(
                    $"Integration event handlers are registered ({string.Join(", ", handlers.Select(HandlerRegistrationValidator.Describe).Distinct())}), "
                    + "but the outbox is not, so nothing would deliver their events. Add it with PhemeOptions.AddOutbox(); "
                    + "integration events are delivered only through the outbox, in-process events are domain events.");
            }

            var named = new List<(string Name, Type EventType)>();
            foreach (Type eventType in GetHandledTypes(descriptors))
            {
                if (eventType.IsAbstract)
                {
                    IEnumerable<ServiceDescriptor> eventHandlers = handlers.Where(
                        descriptor => descriptor.ServiceType == typeof(IIntegrationEventHandler<>).MakeGenericType(eventType));
                    problems.Add(
                        $"Integration event '{TypeNames.Format(eventType)}' is {(eventType.IsInterface ? "an interface" : "abstract")}, "
                        + $"so its handlers {string.Join(", ", eventHandlers.Select(HandlerRegistrationValidator.Describe).Distinct())} never run: "
                        + "the outbox delivers an event only to the handlers of its exact runtime type. Handle the concrete event types.");
                }
                else if (IntegrationEventNames.FindProblem(eventType) is { } problem)
                {
                    problems.Add(problem);
                }
                else
                {
                    named.Add((IntegrationEventNames.Get(eventType), eventType));
                }
            }

            foreach (IGrouping<string, Type> duplicates in named
                .GroupBy(entry => entry.Name, entry => entry.EventType, StringComparer.Ordinal)
                .Where(group => group.Skip(1).Any()))
            {
                problems.Add(
                    $"Integration events {string.Join(", ", duplicates.Select(type => $"'{TypeNames.Format(type)}'"))} have the "
                    + $"same name '{duplicates.Key}', which identifies the event type of a stored message. Give each event a "
                    + "unique name with IntegrationEventName.");
            }

            return problems;
        }

        // The dispatcher resolves handlers without a key, so keyed registrations are never called.
        private static bool IsHandlerRegistration(ServiceDescriptor descriptor)
        {
            return !descriptor.IsKeyedService && MessageContracts.IsIntegrationEventHandlerInterface(descriptor.ServiceType);
        }

        /// <summary>
        /// Gets the event types of the handler registrations, in the order of their first registration.
        /// </summary>
        private static IEnumerable<Type> GetHandledTypes(IEnumerable<ServiceDescriptor> services)
        {
            return services
                .Where(IsHandlerRegistration)
                .Select(descriptor => descriptor.ServiceType.GetGenericArguments()[0])
                .Distinct();
        }
    }
}
