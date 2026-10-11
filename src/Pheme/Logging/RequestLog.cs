using Microsoft.Extensions.Logging;

namespace Pheme.Logging
{
    /// <summary>
    /// The logs written by <see cref="RequestLoggingBehavior{TMessage, TResponse}"/>.
    /// </summary>
    internal static partial class RequestLog
    {
        [LoggerMessage(
            EventId = 1,
            EventName = "MessageHandled",
            Message = "{MessageKind} {MessageType} handled in {ElapsedMs:0.0} ms")]
        public static partial void Handled(ILogger logger, LogLevel level, string messageKind, string messageType, double elapsedMs);

        [LoggerMessage(
            EventId = 2,
            EventName = "MessageExpectedFailure",
            Message = "{MessageKind} {MessageType} failed in {ElapsedMs:0.0} ms with {ExceptionType}: {ExceptionMessage}")]
        public static partial void ExpectedFailure(
            ILogger logger, LogLevel level, string messageKind, string messageType, double elapsedMs, string exceptionType, string exceptionMessage);

        [LoggerMessage(
            EventId = 3,
            EventName = "MessageFailed",
            Message = "{MessageKind} {MessageType} failed in {ElapsedMs:0.0} ms with {ExceptionType}")]
        public static partial void UnexpectedFailure(
            ILogger logger, LogLevel level, string messageKind, string messageType, double elapsedMs, string exceptionType, Exception exception);
    }
}
