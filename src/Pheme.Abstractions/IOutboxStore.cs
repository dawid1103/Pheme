using System.ComponentModel;

namespace Pheme
{
    /// <summary>
    /// Reads and updates the outbox messages of one store: one table of one database, shared by the modules that write
    /// to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For store implementations, not for application code. <c>Pheme.EntityFrameworkCore</c> implements a store for a
    /// <c>DbContext</c> (<c>UseOutbox</c> of <c>AddPhemeDbContext</c>); implement this interface only for another kind
    /// of storage. The outbox processor resolves the stores from a dependency injection scope of its own, a new one for
    /// each read of the due messages and for each update, so a store may depend on scoped services. The
    /// maintenance (<c>IOutboxMaintenance</c>), the retention of processed messages, the health check and the metrics of
    /// the outbox use the other methods, and the test helpers of <c>Pheme.Testing</c> the last two.
    /// </para>
    /// <para>
    /// Guarantees of the outbox, which an implementation has to keep: a message is delivered at least once, not
    /// exactly once; the due messages of a store are tried in the order of <see cref="OutboxMessage.OccurredOnUtc"/>,
    /// then of <see cref="OutboxMessage.Id"/>, so messages of the same time keep one order, but a failed message is
    /// tried again only at its <see cref="OutboxMessage.NextAttemptOnUtc"/>, so the messages after it may be delivered
    /// before it. Every method that returns several messages orders them the same way. A store does not lock the
    /// messages it returns, so only one instance of the application may run the outbox processor; with two, a message
    /// may be delivered twice.
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public interface IOutboxStore
    {
        /// <summary>
        /// Gets the name of this store, unique among the stores of the application and written to
        /// <see cref="OutboxMessage.Source"/> of its messages.
        /// </summary>
        string Source { get; }

        /// <summary>
        /// Gets the oldest due messages: pending - not processed and not parked - with no
        /// <see cref="OutboxMessage.NextAttemptOnUtc"/> or one not after <paramref name="now"/>.
        /// </summary>
        /// <param name="batchSize">The maximum number of messages to return; at least 1.</param>
        /// <param name="maxAttempts">
        /// The number of failed attempts from which a message is parked; messages with an
        /// <see cref="OutboxMessage.AttemptCount"/> of this value or more are not returned.
        /// </param>
        /// <param name="now">The current time of the outbox, compared with <see cref="OutboxMessage.NextAttemptOnUtc"/>.</param>
        /// <param name="cancellationToken">A token to cancel the read.</param>
        /// <returns>
        /// At most <paramref name="batchSize"/> messages, in the order of <see cref="OutboxMessage.OccurredOnUtc"/>,
        /// then of <see cref="OutboxMessage.Id"/>.
        /// </returns>
        Task<IReadOnlyList<OutboxMessage>> GetPending(int batchSize, int maxAttempts, DateTimeOffset now, CancellationToken cancellationToken);

        /// <summary>
        /// Marks a message as processed: sets <see cref="OutboxMessage.ProcessedOnUtc"/> to now and clears
        /// <see cref="OutboxMessage.Error"/>.
        /// </summary>
        /// <param name="messageId">The <see cref="OutboxMessage.Id"/> of the message.</param>
        /// <param name="cancellationToken">A token to cancel the update.</param>
        /// <returns>A task that completes when the change has been saved.</returns>
        Task MarkAsProcessed(Guid messageId, CancellationToken cancellationToken);

        /// <summary>
        /// Records a failed attempt to deliver a message: increments <see cref="OutboxMessage.AttemptCount"/> and
        /// stores the error, the handlers that completed, so the next attempt skips them, and when that attempt is due.
        /// </summary>
        /// <param name="messageId">The <see cref="OutboxMessage.Id"/> of the message.</param>
        /// <param name="errorMessage">The error of the attempt, stored in <see cref="OutboxMessage.Error"/>.</param>
        /// <param name="completedHandlers">
        /// All handlers that have completed so far, including those of earlier attempts, stored as given in
        /// <see cref="OutboxMessage.CompletedHandlers"/>; <see langword="null"/> when none has.
        /// </param>
        /// <param name="nextAttemptOnUtc">
        /// When the next attempt is due, stored in <see cref="OutboxMessage.NextAttemptOnUtc"/>; <see langword="null"/>
        /// when the attempt parks the message.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the update.</param>
        /// <returns>The <see cref="OutboxMessage.AttemptCount"/> of the message after the increment.</returns>
        Task<int> MarkAsFailed(
            Guid messageId,
            string errorMessage,
            string? completedHandlers,
            DateTimeOffset? nextAttemptOnUtc,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets the oldest parked messages: not processed, with <paramref name="maxAttempts"/> or more failed attempts.
        /// </summary>
        /// <param name="limit">The maximum number of messages to return; at least 1.</param>
        /// <param name="maxAttempts">The number of failed attempts from which a message is parked.</param>
        /// <param name="cancellationToken">A token to cancel the read.</param>
        /// <returns>
        /// At most <paramref name="limit"/> messages, in the order of <see cref="OutboxMessage.OccurredOnUtc"/>, then of
        /// <see cref="OutboxMessage.Id"/>.
        /// </returns>
        Task<IReadOnlyList<OutboxMessage>> GetParked(int limit, int maxAttempts, CancellationToken cancellationToken);

        /// <summary>
        /// Makes a message that is not processed pending again, as new and due at once: sets
        /// <see cref="OutboxMessage.AttemptCount"/> to 0 and clears <see cref="OutboxMessage.Error"/> and
        /// <see cref="OutboxMessage.NextAttemptOnUtc"/>, but keeps <see cref="OutboxMessage.CompletedHandlers"/>, so the
        /// handlers that have completed are not called again.
        /// </summary>
        /// <param name="messageId">The <see cref="OutboxMessage.Id"/> of the message.</param>
        /// <param name="cancellationToken">A token to cancel the update.</param>
        /// <returns>
        /// <see langword="true"/> when the message was made pending; <see langword="false"/> when this store has no
        /// message with that identifier that is not processed.
        /// </returns>
        Task<bool> Requeue(Guid messageId, CancellationToken cancellationToken);

        /// <summary>
        /// Deletes the processed messages, those with <see cref="OutboxMessage.ProcessedOnUtc"/> before
        /// <paramref name="processedBefore"/>. Pending and parked messages are never deleted.
        /// </summary>
        /// <param name="processedBefore">The time before which processed messages are deleted.</param>
        /// <param name="cancellationToken">A token to cancel the deletion.</param>
        /// <returns>The number of deleted messages.</returns>
        Task<int> DeleteProcessedBefore(DateTimeOffset processedBefore, CancellationToken cancellationToken);

        /// <summary>
        /// Counts the pending and the parked messages and finds the oldest pending one; a pending message waiting for its
        /// <see cref="OutboxMessage.NextAttemptOnUtc"/> counts as pending.
        /// </summary>
        /// <param name="maxAttempts">The number of failed attempts from which a message is parked.</param>
        /// <param name="cancellationToken">A token to cancel the read.</param>
        /// <returns>The state of the messages of this store.</returns>
        Task<OutboxStats> GetStats(int maxAttempts, CancellationToken cancellationToken);

        /// <summary>
        /// Gets all messages of one type, whatever their state: pending, parked and processed. For the test helpers of
        /// <c>Pheme.Testing</c>; the outbox itself does not use it, so it may read the whole table.
        /// </summary>
        /// <param name="type">The <see cref="OutboxMessage.Type"/> of the messages, compared ordinally.</param>
        /// <param name="cancellationToken">A token to cancel the read.</param>
        /// <returns>
        /// The messages, in the order of <see cref="OutboxMessage.OccurredOnUtc"/>, then of <see cref="OutboxMessage.Id"/>.
        /// </returns>
        Task<IReadOnlyList<OutboxMessage>> GetByType(string type, CancellationToken cancellationToken);

        /// <summary>
        /// Makes a message pending again as if it had just been written, whatever its state: clears
        /// <see cref="OutboxMessage.ProcessedOnUtc"/>, <see cref="OutboxMessage.Error"/>,
        /// <see cref="OutboxMessage.NextAttemptOnUtc"/> and <see cref="OutboxMessage.CompletedHandlers"/> and sets <see cref="OutboxMessage.AttemptCount"/> to 0, so all
        /// its handlers are called again. For the test helpers of <c>Pheme.Testing</c>, which check that handlers are
        /// idempotent; the outbox itself does not use it.
        /// </summary>
        /// <param name="messageId">The <see cref="OutboxMessage.Id"/> of the message.</param>
        /// <param name="cancellationToken">A token to cancel the update.</param>
        /// <returns>
        /// <see langword="true"/> when the message was made pending; <see langword="false"/> when this store has no
        /// message with that identifier.
        /// </returns>
        Task<bool> Redeliver(Guid messageId, CancellationToken cancellationToken);
    }
}
