namespace Pheme.Outbox
{
    /// <summary>
    /// The outcome of delivering one outbox message.
    /// </summary>
    /// <param name="CompletedHandlers">
    /// The names of all handlers that have completed, including those of earlier attempts, for
    /// <see cref="IOutboxStore.MarkAsFailed"/>.
    /// </param>
    /// <param name="Error">The error of the delivery, or <see langword="null"/> when every handler has completed.</param>
    internal sealed record IntegrationEventDispatchResult(IReadOnlyList<string> CompletedHandlers, Exception? Error);
}
