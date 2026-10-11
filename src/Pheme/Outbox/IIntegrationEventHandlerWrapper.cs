using Microsoft.Extensions.DependencyInjection;

namespace Pheme.Outbox
{
    /// <summary>
    /// Deserializes integration events of one type and calls their handlers. Created once per event type, so reflection
    /// runs only on its first delivery.
    /// </summary>
    internal interface IIntegrationEventHandlerWrapper
    {
        /// <summary>
        /// Deserializes the event from <paramref name="content"/> and calls each of its handlers whose name is not in
        /// <paramref name="completedHandlers"/>, each resolved from a new scope of <paramref name="scopeFactory"/>,
        /// adding the names of those that complete.
        /// </summary>
        /// <returns>The failures of the handlers in an <see cref="AggregateException"/>, or <see langword="null"/> when none failed.</returns>
        /// <exception cref="System.Text.Json.JsonException">The content is not valid JSON of the event type.</exception>
        /// <exception cref="InvalidOperationException">The content is <c>null</c>, or a handler cannot be resolved.</exception>
        Task<AggregateException?> Handle(
            string content, IServiceScopeFactory scopeFactory, List<string> completedHandlers, CancellationToken cancellationToken);
    }
}
