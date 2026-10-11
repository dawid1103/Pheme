namespace Pheme
{
    /// <summary>
    /// Inspects and requeues the parked messages of all outbox stores, for example from an administration endpoint, once
    /// the cause of their failure has been fixed.
    /// </summary>
    /// <remarks>
    /// Registered as a scoped service by <c>AddOutbox</c> of <see cref="PhemeOptions"/>; it uses the stores of the scope
    /// it is resolved from. A message is parked after <see cref="OutboxOptions.MaxAttempts"/> failed attempts.
    /// </remarks>
    public interface IOutboxMaintenance
    {
        /// <summary>
        /// Gets the oldest parked messages of all stores, with their <see cref="OutboxMessage.Source"/> and
        /// <see cref="OutboxMessage.Error"/>.
        /// </summary>
        /// <param name="limit">The maximum number of messages to return. The default is 100.</param>
        /// <param name="cancellationToken">A token to cancel the read.</param>
        /// <returns>
        /// At most <paramref name="limit"/> messages, in the order of <see cref="OutboxMessage.OccurredOnUtc"/>, then of
        /// <see cref="OutboxMessage.Id"/>.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is less than 1.</exception>
        Task<IReadOnlyList<OutboxMessage>> GetParked(int limit = 100, CancellationToken cancellationToken = default);

        /// <summary>
        /// Makes a message that is not processed, usually a parked one, pending again with no failed attempts, due at
        /// once. The handlers that have completed are not called again.
        /// </summary>
        /// <param name="messageId">The <see cref="OutboxMessage.Id"/> of the message.</param>
        /// <param name="cancellationToken">A token to cancel the update.</param>
        /// <returns>
        /// <see langword="true"/> when a store had the message; <see langword="false"/> when none has a message with
        /// that identifier that is not processed.
        /// </returns>
        Task<bool> Requeue(Guid messageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Makes all parked messages pending again with no failed attempts, due at once. The handlers that have completed
        /// are not called again.
        /// </summary>
        /// <param name="source">
        /// The <see cref="OutboxMessage.Source"/> of the store whose messages to requeue, or <see langword="null"/> for
        /// all stores.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the update.</param>
        /// <returns>The number of requeued messages.</returns>
        /// <exception cref="ArgumentException">No store has the name <paramref name="source"/>.</exception>
        Task<int> RequeueAllParked(string? source = null, CancellationToken cancellationToken = default);
    }
}
