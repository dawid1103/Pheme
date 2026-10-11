using System.Collections;
using System.Collections.Concurrent;
using Pheme.Diagnostics;
using Pheme.Internal;

namespace Pheme.DomainEvents
{
    /// <summary>
    /// Resolves domain event handlers from the service provider it was resolved from. Wrappers are cached per event type
    /// for all publishers.
    /// </summary>
    internal sealed class DomainEventPublisher(IServiceProvider serviceProvider, MessageInstrumentation instrumentation) : IDomainEventPublisher
    {
        private static readonly ConcurrentDictionary<Type, IDomainEventHandlerWrapper> Wrappers = new();

        public Task Publish(object domainEvent, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(domainEvent);
            ThrowIfCollection(domainEvent, nameof(domainEvent));

            return Dispatch(domainEvent, cancellationToken);
        }

        public Task Publish(IEnumerable<object> domainEvents, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(domainEvents);

            // A copy, so a handler adding events to the source collection does not break the enumeration.
            object[] events = [.. domainEvents];
            foreach (object domainEvent in events)
            {
                if (domainEvent is null)
                {
                    throw new ArgumentException("The domain events contain null.", nameof(domainEvents));
                }

                ThrowIfCollection(domainEvent, nameof(domainEvents));
            }

            return PublishAll(events, cancellationToken);
        }

        private async Task PublishAll(object[] events, CancellationToken cancellationToken)
        {
            foreach (object domainEvent in events)
            {
                await Dispatch(domainEvent, cancellationToken);
            }
        }

        private Task Dispatch(object domainEvent, CancellationToken cancellationToken)
        {
            IDomainEventHandlerWrapper wrapper = Wrappers.GetOrAdd(
                domainEvent.GetType(),
                static eventType => (IDomainEventHandlerWrapper)Activator.CreateInstance(
                    typeof(DomainEventHandlerWrapper<>).MakeGenericType(eventType))!);
            return wrapper.Handle(domainEvent, serviceProvider, instrumentation, cancellationToken);
        }

        // A list of struct events is not an IEnumerable<object>, so it binds to Publish(object) and would be dispatched as
        // one event of type List<T>, which has no handlers: every event would be lost without an error.
        private static void ThrowIfCollection(object domainEvent, string parameterName)
        {
            if (domainEvent is IEnumerable)
            {
                throw new ArgumentException(
                    $"'{TypeNames.Format(domainEvent.GetType())}' is a collection, not a domain event. Publish the events it "
                    + "contains with Publish(IEnumerable<object>), for example events.Cast<object>().",
                    parameterName);
            }
        }
    }
}
