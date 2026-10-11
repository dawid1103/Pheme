namespace Pheme
{
    /// <summary>
    /// The outcome of one run of the outbox: <see cref="IOutboxRunner.RunOnce"/>, or a cycle of the outbox processor.
    /// </summary>
    public sealed class OutboxRunResult
    {
        internal OutboxRunResult(int processed, IReadOnlyList<OutboxMessage> failed, bool hasMore)
        {
            Processed = processed;
            Failed = failed;
            HasMore = hasMore;
        }

        /// <summary>
        /// Gets how many messages were delivered to all their handlers and marked as processed.
        /// </summary>
        public int Processed { get; }

        /// <summary>
        /// Gets the messages whose delivery failed, with <see cref="OutboxMessage.Error"/>,
        /// <see cref="OutboxMessage.AttemptCount"/>, <see cref="OutboxMessage.CompletedHandlers"/> and
        /// <see cref="OutboxMessage.NextAttemptOnUtc"/> as recorded by this run. A message with
        /// <see cref="OutboxOptions.MaxAttempts"/> attempts is parked.
        /// </summary>
        public IReadOnlyList<OutboxMessage> Failed { get; }

        /// <summary>
        /// Gets whether a store returned a full batch (<see cref="OutboxOptions.BatchSize"/>), so more messages may be
        /// due. Failed messages below <see cref="OutboxOptions.MaxAttempts"/> stay pending as well, due again at their
        /// <see cref="OutboxMessage.NextAttemptOnUtc"/>.
        /// </summary>
        public bool HasMore { get; }
    }
}
