namespace Pheme
{
    /// <summary>
    /// The state of the messages of one outbox store, from <see cref="IOutboxStore.GetStats"/>.
    /// </summary>
    public sealed class OutboxStats
    {
        /// <summary>
        /// Gets how many messages are pending: not processed and not parked, also those waiting for their
        /// <see cref="OutboxMessage.NextAttemptOnUtc"/>.
        /// </summary>
        public required int Pending { get; init; }

        /// <summary>
        /// Gets how many messages are parked: not processed, with as many failed attempts as the outbox allows.
        /// </summary>
        public required int Parked { get; init; }

        /// <summary>
        /// Gets the <see cref="OutboxMessage.OccurredOnUtc"/> of the oldest pending message, or <see langword="null"/>
        /// when none is pending.
        /// </summary>
        public required DateTimeOffset? OldestPendingOccurredOnUtc { get; init; }
    }
}
