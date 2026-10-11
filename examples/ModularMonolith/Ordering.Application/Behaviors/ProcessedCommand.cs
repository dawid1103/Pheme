namespace Ordering.Application.Behaviors
{
    /// <summary>
    /// A command of the ordering module handled with an idempotency key, and its response.
    /// </summary>
    public sealed class ProcessedCommand
    {
        private ProcessedCommand(Guid idempotencyKey, string response, DateTimeOffset processedOnUtc)
        {
            IdempotencyKey = idempotencyKey;
            Response = response;
            ProcessedOnUtc = processedOnUtc;
        }

        /// <summary>
        /// The key the client sent, unique within the module.
        /// </summary>
        public Guid IdempotencyKey { get; private set; }

        /// <summary>
        /// The JSON of the response.
        /// </summary>
        public string Response { get; private set; }

        /// <summary>
        /// When the command was handled.
        /// </summary>
        public DateTimeOffset ProcessedOnUtc { get; private set; }

        /// <summary>
        /// Records the command handled with <paramref name="idempotencyKey"/>.
        /// </summary>
        public static ProcessedCommand Record(Guid idempotencyKey, string response, DateTimeOffset processedOnUtc) =>
            new(idempotencyKey, response, processedOnUtc);
    }
}
