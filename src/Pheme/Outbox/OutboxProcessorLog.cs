using Microsoft.Extensions.Logging;

namespace Pheme.Outbox
{
    /// <summary>
    /// The logs of the outbox runs, in the category <see cref="OutboxOptions.CategoryName"/>.
    /// </summary>
    internal static partial class OutboxProcessorLog
    {
        [LoggerMessage(
            EventId = 1,
            EventName = "OutboxMessageFailed",
            Level = LogLevel.Warning,
            Message = "Integration event {EventType} {MessageId} from outbox {Source} failed on attempt {AttemptCount} of {MaxAttempts} and will be retried in {RetryDelay}")]
        public static partial void Failed(
            ILogger logger, string eventType, Guid messageId, string source, int attemptCount, int maxAttempts, TimeSpan retryDelay, Exception exception);

        [LoggerMessage(
            EventId = 2,
            EventName = "OutboxMessageParked",
            Level = LogLevel.Error,
            Message = "Integration event {EventType} {MessageId} from outbox {Source} failed {AttemptCount} times and is parked")]
        public static partial void Parked(
            ILogger logger, string eventType, Guid messageId, string source, int attemptCount, Exception exception);

        [LoggerMessage(
            EventId = 3,
            EventName = "OutboxRunFailed",
            Level = LogLevel.Error,
            Message = "The outbox processor failed to run and tries again in {PollingInterval}")]
        public static partial void RunFailed(ILogger logger, TimeSpan pollingInterval, Exception exception);

        [LoggerMessage(
            EventId = 4,
            EventName = "OutboxRetentionFailed",
            Level = LogLevel.Error,
            Message = "The outbox processor failed to delete the processed messages of outbox {Source}")]
        public static partial void RetentionFailed(ILogger logger, string source, Exception exception);

        [LoggerMessage(
            EventId = 5,
            EventName = "OutboxRetentionCompleted",
            Message = "Deleted {DeletedCount} processed messages older than {ProcessedBefore} from outbox {Source}; the next deletion is due at {NextRun}")]
        public static partial void RetentionCompleted(
            ILogger logger, LogLevel level, int deletedCount, DateTimeOffset processedBefore, string source, DateTimeOffset nextRun);
    }
}
