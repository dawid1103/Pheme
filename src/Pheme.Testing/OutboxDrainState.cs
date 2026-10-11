namespace Pheme.Testing
{
    /// <summary>
    /// The failed messages <c>DrainOutbox</c> has reported for one service provider, so each fails only the test that
    /// first saw it fail, also when the tests share the application and its outbox.
    /// </summary>
    internal sealed class OutboxDrainState
    {
        private readonly HashSet<Guid> reported = [];

        /// <summary>
        /// Returns the messages of <paramref name="failed"/> not reported before, now reported.
        /// </summary>
        public OutboxMessage[] Report(IReadOnlyList<OutboxMessage> failed)
        {
            lock (reported)
            {
                return [.. failed.Where(message => reported.Add(message.Id))];
            }
        }

        /// <summary>
        /// Reports a later failure of the message again, as after its redelivery.
        /// </summary>
        public void Forget(Guid messageId)
        {
            lock (reported)
            {
                reported.Remove(messageId);
            }
        }
    }
}
