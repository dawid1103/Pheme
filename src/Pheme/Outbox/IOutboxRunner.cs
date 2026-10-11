namespace Pheme
{
    /// <summary>
    /// Delivers the pending outbox messages of all stores once, for integration tests and applications that deliver
    /// on a schedule of their own instead of with the outbox processor.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton by <c>AddOutbox</c> of <see cref="PhemeOptions"/>. Runs of one application never
    /// overlap: a call waits for the run in progress, also for one of the outbox processor. Set
    /// <see cref="OutboxOptions.ProcessInBackground"/> to <see langword="false"/> when calling it, so the processor does
    /// not deliver the messages first.
    /// </remarks>
    public interface IOutboxRunner
    {
        /// <summary>
        /// Reads a batch of due messages from each outbox store - pending, and failed ones whose
        /// <see cref="OutboxMessage.NextAttemptOnUtc"/> has come - and delivers them, as one cycle of the outbox processor
        /// does.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token passed to the handlers. Once it is cancelled, a failed delivery is not recorded as an attempt and the
        /// run ends with <see cref="OperationCanceledException"/>; the message is delivered again by a later run.
        /// </param>
        /// <returns>The outcome of the run.</returns>
        /// <remarks>
        /// Each handler of a message is called in a dependency injection scope of its own, and the outcome is saved in
        /// another new scope, so the services of a failed handler, such as a <c>DbContext</c> with its changes, are not
        /// reused. A failed message is due again after <see cref="OutboxOptions.RetryDelay"/>, growing with its attempts.
        /// </remarks>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task<OutboxRunResult> RunOnce(CancellationToken cancellationToken = default);
    }
}
