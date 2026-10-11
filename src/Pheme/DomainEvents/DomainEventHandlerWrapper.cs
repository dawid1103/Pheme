using Microsoft.Extensions.DependencyInjection;
using Pheme.Diagnostics;
using Pheme.Internal;

namespace Pheme.DomainEvents
{
    /// <summary>
    /// Runs the handlers of <typeparamref name="TEvent"/> one after another, inside the event's activity and duration
    /// measurement.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type.</typeparam>
    internal sealed class DomainEventHandlerWrapper<TEvent> : IDomainEventHandlerWrapper
        where TEvent : notnull
    {
        private static readonly string EventType = TypeNames.Format(typeof(TEvent));

        public async Task Handle(
            object domainEvent, IServiceProvider serviceProvider, MessageInstrumentation instrumentation, CancellationToken cancellationToken)
        {
            var typedEvent = (TEvent)domainEvent;
            MessageMeasurement measurement = instrumentation.Start(EventType, PhemeDiagnostics.DomainEventKind);
            try
            {
                // The same class registered twice, for example by hand after the scan, would handle the event twice.
                HashSet<Type>? handled = null;
                foreach (IDomainEventHandler<TEvent> handler in serviceProvider.GetServices<IDomainEventHandler<TEvent>>())
                {
                    if ((handled ??= []).Add(handler.GetType()))
                    {
                        await handler.Handle(typedEvent, cancellationToken);
                    }
                }
            }
            catch (Exception exception)
            {
                measurement.Stop(exception);
                throw;
            }

            measurement.Stop();
        }
    }
}
