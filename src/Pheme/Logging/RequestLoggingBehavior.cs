using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pheme.Internal;

namespace Pheme.Logging
{
    /// <summary>
    /// Logs how the handling of every message ended and how long it took. Always the outermost behavior; a singleton
    /// per message type.
    /// </summary>
    /// <remarks>
    /// Never changes the outcome: a failure is logged in an exception filter, so the exception reaches the caller
    /// unchanged, and an exception thrown while logging a success is ignored.
    /// </remarks>
    internal sealed class RequestLoggingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        private static readonly string MessageType = TypeNames.Format(typeof(TMessage));
        private static readonly string MessageKind = MessageContracts.GetMessageKindOf(typeof(TMessage));

        private readonly ILogger logger;
        private readonly RequestLoggingOptions options;

        public RequestLoggingBehavior(ILoggerFactory loggerFactory, IOptions<RequestLoggingOptions> options)
        {
            logger = loggerFactory.CreateLogger(RequestLoggingOptions.CategoryName);
            this.options = options.Value;
        }

        public async Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            TResponse response;
            try
            {
                response = await next(cancellationToken);
            }
            catch (Exception exception) when (LogFailure(exception, startTimestamp, cancellationToken))
            {
                // Not reached: the filter returns false, so the exception propagates with its stack trace untouched,
                // also when the IsExpected predicate or a logger provider throws.
                throw;
            }

            double elapsedMs = GetElapsedMs(startTimestamp);
            try
            {
                RequestLog.Handled(logger, options.SuccessLevel, MessageKind, MessageType, elapsedMs);
            }
            catch (Exception)
            {
                // Logging must never fail an operation that has already completed, maybe committed.
            }

            return response;
        }

        private static double GetElapsedMs(long startTimestamp)
        {
            return Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        }

        private bool LogFailure(Exception exception, long startTimestamp, CancellationToken cancellationToken)
        {
            double elapsedMs = GetElapsedMs(startTimestamp);
            string exceptionType = TypeNames.Format(exception.GetType());
            bool canceledByCaller = exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
            if (canceledByCaller || options.IsExpected(exception))
            {
                RequestLog.ExpectedFailure(
                    logger, options.ExpectedFailureLevel, MessageKind, MessageType, elapsedMs, exceptionType, exception.Message);
            }
            else
            {
                RequestLog.UnexpectedFailure(
                    logger, options.UnexpectedFailureLevel, MessageKind, MessageType, elapsedMs, exceptionType, exception);
            }

            return false;
        }
    }
}
