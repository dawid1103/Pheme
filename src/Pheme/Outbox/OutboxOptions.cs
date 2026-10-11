using Microsoft.Extensions.Logging;

namespace Pheme
{
    /// <summary>
    /// Configures how the outbox processor delivers the messages of all outbox stores; set in <c>AddOutbox</c> of
    /// <see cref="PhemeOptions"/>.
    /// </summary>
    /// <remarks>
    /// Delivery is at least once, and only one instance of the application may run the processor: the stores do not
    /// lock the messages they return, so with two a message may be delivered twice. See <see cref="IOutboxStore"/> for the order of delivery.
    /// </remarks>
    public sealed class OutboxOptions
    {
        /// <summary>
        /// The category of the outbox processor's logs: <c>Pheme.Outbox</c>.
        /// </summary>
        public const string CategoryName = "Pheme.Outbox";

        private TimeSpan pollingInterval = TimeSpan.FromSeconds(5);
        private int batchSize = 20;
        private int maxAttempts = 10;
        private TimeSpan retryDelay = TimeSpan.FromSeconds(10);
        private TimeSpan maxRetryDelay = TimeSpan.FromMinutes(15);
        private TimeSpan? processedRetention = TimeSpan.FromDays(30);
        private LogLevel retentionLogLevel = LogLevel.Information;

        /// <summary>
        /// Gets or sets how long the processor waits before it looks for new messages again. The default is 5 seconds.
        /// </summary>
        /// <remarks>
        /// The processor waits only when no store returned a full batch; with a backlog it goes on with the next batch at
        /// once. A failed message is tried again once its <see cref="RetryDelay"/> has passed, at the first run after
        /// that, so it waits up to this interval longer.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
        public TimeSpan PollingInterval
        {
            get => pollingInterval;
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
                pollingInterval = value;
            }
        }

        /// <summary>
        /// Gets or sets how many due messages the processor reads from each store at a time. The default is 20.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
        public int BatchSize
        {
            get => batchSize;
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
                batchSize = value;
            }
        }

        /// <summary>
        /// Gets or sets after how many failed attempts a message is parked: no longer delivered, kept with its error
        /// for inspection until it is requeued (<see cref="IOutboxMaintenance"/>). The default is 10.
        /// </summary>
        /// <remarks>
        /// With the default delays a message is parked after about 50 minutes of failures: 10 seconds after the first
        /// failed attempt, doubled after each next one up to 15 minutes (<see cref="RetryDelay"/>,
        /// <see cref="MaxRetryDelay"/>). A parked message does not block the messages after it. Parking is logged as an
        /// error, the other failed attempts as warnings, in the category <see cref="CategoryName"/>.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
        public int MaxAttempts
        {
            get => maxAttempts;
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
                maxAttempts = value;
            }
        }

        /// <summary>
        /// Gets or sets how long a message waits for its next attempt after its first failed one; doubled after each
        /// next failed attempt, up to <see cref="MaxRetryDelay"/>. The default is 10 seconds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The time of the next attempt is stored with the message (<see cref="OutboxMessage.NextAttemptOnUtc"/>), and
        /// until then the message is not read, so a message that keeps failing does not hold up the messages after it,
        /// and an outage of what the handlers call, such as a mail server, is outlasted before the message is parked.
        /// </para>
        /// <para>
        /// <see cref="TimeSpan.Zero"/> makes a failed message due at once, tried again by the next run, as
        /// <c>UsePhemeOutboxForTests</c> of <c>Pheme.Testing</c> sets it; the processor then waits
        /// <see cref="PollingInterval"/> after a run with a failure, also with a full batch, so a failing message does
        /// not use up its attempts in a tight loop.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public TimeSpan RetryDelay
        {
            get => retryDelay;
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
                retryDelay = value;
            }
        }

        /// <summary>
        /// Gets or sets the longest wait for the next attempt of a failed message, however many attempts have failed.
        /// The default is 15 minutes.
        /// </summary>
        /// <remarks>
        /// Bounds how late a message is delivered once what its handlers call works again. A value below
        /// <see cref="RetryDelay"/> makes every wait this long.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
        public TimeSpan MaxRetryDelay
        {
            get => maxRetryDelay;
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
                maxRetryDelay = value;
            }
        }

        /// <summary>
        /// Gets or sets whether the outbox processor delivers the messages in the background, a hosted service started
        /// with the host. The default is <see langword="true"/>.
        /// </summary>
        /// <remarks>
        /// With <see langword="false"/> the processor does not run, so it deletes no processed messages either, and
        /// messages are delivered only by <see cref="IOutboxRunner.RunOnce"/>: in integration tests, which deliver at chosen points instead of
        /// waiting for the polling, or in applications with a schedule of their own.
        /// </remarks>
        public bool ProcessInBackground { get; set; } = true;

        /// <summary>
        /// Gets or sets how long processed messages are kept before the processor deletes them; <see langword="null"/>
        /// keeps them forever. The default is 30 days.
        /// </summary>
        /// <remarks>
        /// The processor deletes them when it starts and then once an hour, while <see cref="ProcessInBackground"/> is
        /// on, and logs each deletion (<see cref="RetentionLogLevel"/>). Pending and parked messages are never deleted.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
        public TimeSpan? ProcessedRetention
        {
            get => processedRetention;
            set
            {
                if (value is { } retention)
                {
                    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero, nameof(value));
                }

                processedRetention = value;
            }
        }

        /// <summary>
        /// Gets or sets the level of the log written for each store after the processor deleted its processed messages:
        /// the outbox, how many messages it deleted and when the next deletion is due. The default is
        /// <see cref="LogLevel.Information"/>; <see cref="LogLevel.None"/> logs nothing.
        /// </summary>
        /// <remarks>
        /// It is written even when nothing was deleted, in the category <see cref="CategoryName"/>. A deletion that
        /// fails is always logged as an error.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="LogLevel"/>.</exception>
        public LogLevel RetentionLogLevel
        {
            get => retentionLogLevel;
            set
            {
                if (!Enum.IsDefined(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown log level.");
                }

                retentionLogLevel = value;
            }
        }

        /// <summary>
        /// Gets how long a message waits for its next attempt after <paramref name="attemptCount"/> failed ones:
        /// <see cref="RetryDelay"/> doubled for each failed attempt after the first, at most <see cref="MaxRetryDelay"/>.
        /// </summary>
        internal TimeSpan GetRetryDelay(int attemptCount)
        {
            // In double, with at most 64 doublings: more would exceed any TimeSpan for a delay of one tick, and a zero
            // delay stays zero instead of becoming zero times infinity.
            double ticks = Math.ScaleB(RetryDelay.Ticks, Math.Min(attemptCount - 1, 64));
            return ticks < MaxRetryDelay.Ticks ? TimeSpan.FromTicks((long)ticks) : MaxRetryDelay;
        }
    }
}
