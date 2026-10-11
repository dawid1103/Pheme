using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pheme.Internal;

namespace Pheme.Outbox
{
    /// <summary>
    /// Calls the handlers of <typeparamref name="TEvent"/> one after another, each in a scope of its own; a failed handler
    /// does not stop the others unless the delivery is cancelled.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type.</typeparam>
    internal sealed class IntegrationEventHandlerWrapper<TEvent> : IIntegrationEventHandlerWrapper
        where TEvent : IIntegrationEvent
    {
        // The names recorded as completed, formatted once per handler type.
        private static readonly ConcurrentDictionary<Type, string> HandlerNames = new();

        public async Task<AggregateException?> Handle(
            string content, IServiceScopeFactory scopeFactory, List<string> completedHandlers, CancellationToken cancellationToken)
        {
            TEvent integrationEvent = JsonSerializer.Deserialize<TEvent>(content, OutboxSerialization.Options)
                ?? throw new InvalidOperationException($"The content of integration event '{TypeNames.Format(typeof(TEvent))}' is null.");

            // The handler classes, read from a scope of their own, as registrations by hand may use a factory. The same
            // class registered twice, for example by hand after the scan, handles the event once.
            Type[] handlerTypes;
            await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
            {
                handlerTypes = [.. scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>().Select(handler => handler.GetType()).Distinct()];
            }

            List<Exception>? failures = null;
            foreach (Type handlerType in handlerTypes)
            {
                string name = HandlerNames.GetOrAdd(handlerType, TypeNames.FormatStored);
                if (completedHandlers.Contains(name))
                {
                    continue;
                }

                try
                {
                    // A scope per handler: what a failed handler left in its DbContext is never saved by another one.
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    IIntegrationEventHandler<TEvent> handler = scope.ServiceProvider
                        .GetServices<IIntegrationEventHandler<TEvent>>()
                        .First(candidate => candidate.GetType() == handlerType);
                    await handler.Handle(integrationEvent, cancellationToken);
                    completedHandlers.Add(name);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);

                    // The delivery stops: the remaining handlers would only see the cancelled token.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }

            return failures is null ? null : new AggregateException(failures);
        }
    }
}
