using Microsoft.Extensions.Logging;

namespace Pheme
{
    /// <summary>
    /// Configures the request logging added by <see cref="PhemeOptions.AddRequestLogging()"/>: the level of each outcome
    /// and which exceptions are expected.
    /// </summary>
    /// <remarks>
    /// One instance configures the logging of all messages: every <c>AddRequestLogging</c> call configures it in call
    /// order, and <c>services.Configure&lt;RequestLoggingOptions&gt;</c> may be used as well.
    /// </remarks>
    public sealed class RequestLoggingOptions
    {
        /// <summary>
        /// The category of the request logs, for log filters such as <c>Logging:LogLevel:Pheme.Requests</c>.
        /// </summary>
        public const string CategoryName = "Pheme.Requests";

        private LogLevel successLevel = LogLevel.Debug;
        private LogLevel expectedFailureLevel = LogLevel.Information;
        private LogLevel unexpectedFailureLevel = LogLevel.Error;
        private Func<Exception, bool> isExpected = static _ => false;

        /// <summary>
        /// Gets or sets the level of the log written when a message is handled. The default is
        /// <see cref="LogLevel.Debug"/>; <see cref="LogLevel.None"/> logs nothing.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="LogLevel"/>.</exception>
        public LogLevel SuccessLevel
        {
            get => successLevel;
            set => successLevel = Validate(value);
        }

        /// <summary>
        /// Gets or sets the level of the log written when handling fails with an expected exception, logged without
        /// its stack trace. The default is <see cref="LogLevel.Information"/>; <see cref="LogLevel.None"/> logs nothing.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="LogLevel"/>.</exception>
        public LogLevel ExpectedFailureLevel
        {
            get => expectedFailureLevel;
            set => expectedFailureLevel = Validate(value);
        }

        /// <summary>
        /// Gets or sets the level of the log written when handling fails with any other exception, logged with the
        /// exception. The default is <see cref="LogLevel.Error"/>; <see cref="LogLevel.None"/> logs nothing.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="LogLevel"/>.</exception>
        public LogLevel UnexpectedFailureLevel
        {
            get => unexpectedFailureLevel;
            set => unexpectedFailureLevel = Validate(value);
        }

        /// <summary>
        /// Gets or sets which exceptions are an expected outcome rather than an error, such as validation and domain
        /// exceptions. The default expects none.
        /// </summary>
        /// <remarks>
        /// An <see cref="OperationCanceledException"/> thrown after the caller's cancellation token was canceled is
        /// always expected. To extend a predicate set by another call, capture it:
        /// <c>var previous = logging.IsExpected; logging.IsExpected = e =&gt; previous(e) || e is MyException;</c>.
        /// An exception thrown by the predicate is ignored and the message is not logged.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
        public Func<Exception, bool> IsExpected
        {
            get => isExpected;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                isExpected = value;
            }
        }

        // Named like the setter parameter, so the exception names it.
        private static LogLevel Validate(LogLevel value)
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown log level.");
            }

            return value;
        }
    }
}
