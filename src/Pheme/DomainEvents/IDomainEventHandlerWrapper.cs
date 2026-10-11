using Pheme.Diagnostics;

namespace Pheme.DomainEvents
{
    /// <summary>
    /// Dispatches domain events of one type to their handlers. Created once per event type, so reflection runs only on
    /// its first publication.
    /// </summary>
    internal interface IDomainEventHandlerWrapper
    {
        Task Handle(
            object domainEvent, IServiceProvider serviceProvider, MessageInstrumentation instrumentation, CancellationToken cancellationToken);
    }
}
