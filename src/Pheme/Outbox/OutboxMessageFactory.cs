using System.Diagnostics;
using System.Text.Json;
using Pheme.Internal;

namespace Pheme.Outbox
{
    /// <summary>
    /// Creates outbox messages in the one format the dispatcher reads, only for events the application handles.
    /// </summary>
    internal sealed class OutboxMessageFactory(IntegrationEventTypes types) : IOutboxMessageFactory
    {
        public OutboxMessage Create(IIntegrationEvent integrationEvent, string source)
        {
            ArgumentNullException.ThrowIfNull(integrationEvent);
            ArgumentException.ThrowIfNullOrWhiteSpace(source);

            Type eventType = integrationEvent.GetType();
            string type = IntegrationEventNames.Get(eventType);

            // The writer and the handlers are in one application: an event without a handler is a mistake, which would
            // be parked after all its attempts, in production instead of in the request that writes it.
            if (types.Find(type) != eventType)
            {
                throw new InvalidOperationException(
                    $"No handler of integration event '{TypeNames.Format(eventType)}' is registered, so the outbox would "
                    + "park it. Add a handler of the event, IIntegrationEventHandler<"
                    + $"{TypeNames.Format(eventType)}>, in an assembly added with AddHandlersFromAssemblies, or do not write it.");
            }

            return new OutboxMessage
            {
                Id = integrationEvent.EventId,
                Type = type,
                Content = JsonSerializer.Serialize(integrationEvent, eventType, OutboxSerialization.Options),
                Source = source,
                // Npgsql writes only a zero offset to timestamptz.
                OccurredOnUtc = integrationEvent.OccurredOn.ToUniversalTime(),
                // The delivery links to this context; Activity.Id is a traceparent only in the W3C format.
                TraceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null,
            };
        }
    }
}
