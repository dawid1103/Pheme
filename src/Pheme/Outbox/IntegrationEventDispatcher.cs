using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pheme.Registration;

namespace Pheme.Outbox
{
    /// <summary>
    /// Delivers the integration event of one outbox message to its handlers. Wrappers are cached per event type.
    /// </summary>
    internal sealed class IntegrationEventDispatcher
    {
        private static readonly ConcurrentDictionary<Type, IIntegrationEventHandlerWrapper> Wrappers = new();

        private readonly IntegrationEventTypes types;
        private readonly IServiceScopeFactory scopeFactory;

        public IntegrationEventDispatcher(
            IntegrationEventTypes types, IServiceScopeFactory scopeFactory, IOptionsMonitor<HandlerRegistrationOptions> handlerRegistrations)
        {
            // Validates the handler registrations when no host has done it on start, as the sender does.
            _ = handlerRegistrations.CurrentValue;
            this.types = types;
            this.scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Deserializes the event of a message and calls its handlers, except the completed ones, each in a new scope.
        /// </summary>
        /// <param name="type">The <see cref="OutboxMessage.Type"/> of the message.</param>
        /// <param name="content">The <see cref="OutboxMessage.Content"/> of the message.</param>
        /// <param name="completedHandlers">The names of the handlers that completed in earlier attempts, not called again.</param>
        /// <param name="cancellationToken">A token passed to the handlers; once cancelled, a failed handler stops the delivery.</param>
        /// <returns>
        /// All completed handlers, of earlier attempts too, and the error of the delivery: an unknown type, content that
        /// does not deserialize, a handler that cannot be resolved, or an <see cref="AggregateException"/> of the handlers
        /// that failed. Never throws for a failed delivery.
        /// </returns>
        public async Task<IntegrationEventDispatchResult> Dispatch(
            string type,
            string content,
            IReadOnlyCollection<string> completedHandlers,
            CancellationToken cancellationToken)
        {
            List<string> completed = [.. completedHandlers];
            if (types.Find(type) is not { } eventType)
            {
                return new IntegrationEventDispatchResult(
                    completed,
                    new InvalidOperationException(
                        $"No integration event handled by this application is named '{type}'. Is the assembly of its handlers "
                        + "added with AddHandlersFromAssemblies, or has the event type been renamed, moved or given another "
                        + "IntegrationEventName?"));
            }

            IIntegrationEventHandlerWrapper wrapper = Wrappers.GetOrAdd(
                eventType,
                static eventType => (IIntegrationEventHandlerWrapper)Activator.CreateInstance(
                    typeof(IntegrationEventHandlerWrapper<>).MakeGenericType(eventType))!);
            try
            {
                return new IntegrationEventDispatchResult(
                    completed, await wrapper.Handle(content, scopeFactory, completed, cancellationToken));
            }
            catch (Exception exception)
            {
                return new IntegrationEventDispatchResult(completed, exception);
            }
        }
    }
}
