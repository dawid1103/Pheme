using System.Collections.Concurrent;
using System.Reflection;
using Pheme.Internal;

namespace Pheme.Outbox
{
    /// <summary>
    /// The names integration event types are stored under in outbox messages: the name of
    /// <see cref="IntegrationEventNameAttribute"/>, otherwise the full type name. One rule for the messages written and
    /// for the types the startup validation maps the stored names to.
    /// </summary>
    internal static class IntegrationEventNames
    {
        private static readonly ConcurrentDictionary<Type, string> Names = new();

        /// <summary>
        /// Gets the stored name of <paramref name="eventType"/>, cached per type.
        /// </summary>
        /// <exception cref="InvalidOperationException">The type has no valid name; see <see cref="FindProblem"/>.</exception>
        public static string Get(Type eventType)
        {
            if (Names.TryGetValue(eventType, out string? name))
            {
                return name;
            }

            if (FindProblem(eventType) is { } problem)
            {
                throw new InvalidOperationException(problem);
            }

            return Names.GetOrAdd(eventType, GetName(eventType));
        }

        /// <summary>
        /// Describes why <paramref name="eventType"/> cannot be stored, or returns <see langword="null"/> when it can: a
        /// generic type, whose full name contains the assembly versions of its type arguments and whose attribute name
        /// would be shared by all its constructions, or an empty name in the attribute.
        /// </summary>
        public static string? FindProblem(Type eventType)
        {
            if (eventType.IsGenericType)
            {
                return $"Integration event '{TypeNames.Format(eventType)}' is generic. The outbox stores an event under the "
                    + "name of its type, which for a generic type depends on its type arguments; use a non-generic type.";
            }

            if (string.IsNullOrWhiteSpace(GetName(eventType)))
            {
                return $"Integration event '{TypeNames.Format(eventType)}' has an empty IntegrationEventName. Give it a "
                    + "stable name, for example [IntegrationEventName(\"ordering.order-placed\")], or remove the attribute "
                    + "to use the full type name.";
            }

            return null;
        }

        private static string GetName(Type eventType)
        {
            return eventType.GetCustomAttribute<IntegrationEventNameAttribute>(inherit: false) is { } attribute
                ? attribute.Name
                : eventType.FullName!;
        }
    }
}
