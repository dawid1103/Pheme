using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pheme.Diagnostics
{
    /// <summary>
    /// Starts the activity and the duration measurement of a message, domain event or outbox delivery, as described in
    /// <see cref="PhemeDiagnostics"/>. A singleton per service provider, owning the meter when no
    /// <see cref="IMeterFactory"/> is registered.
    /// </summary>
    internal sealed class MessageInstrumentation : IDisposable
    {
        private static readonly string? Version = typeof(MessageInstrumentation).Assembly.GetName().Version?.ToString();
        private static readonly ActivitySource ActivitySource = new(PhemeDiagnostics.ActivitySourceName, Version);

        // In seconds, as in ASP.NET Core: the default buckets of OpenTelemetry are meant for milliseconds.
        private static readonly InstrumentAdvice<double> DurationAdvice = new()
        {
            HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10],
        };

        private readonly Meter? ownMeter;
        private readonly Histogram<double> duration;

        public MessageInstrumentation(IMeterFactory? meterFactory = null)
        {
            Meter = meterFactory?.Create(PhemeDiagnostics.MeterName, Version)
                ?? (ownMeter = new Meter(PhemeDiagnostics.MeterName, Version));
            duration = Meter.CreateHistogram(
                PhemeDiagnostics.MessageDurationName, "s", "How long the handling of a message took.", advice: DurationAdvice);
        }

        /// <summary>
        /// Gets the meter named <see cref="PhemeDiagnostics.MeterName"/> of this service provider.
        /// </summary>
        public Meter Meter { get; }

        /// <summary>
        /// Starts the activity of a message, a child of the current one, and its duration measurement.
        /// </summary>
        public MessageMeasurement Start(string messageType, string messageKind)
        {
            return Measure(ActivitySource.StartActivity(messageType), messageType, messageKind);
        }

        /// <summary>
        /// Starts the activity of an outbox message's delivery, a child of the current one with a link to the activity
        /// that wrote the message, and its duration measurement.
        /// </summary>
        /// <param name="eventType">The stored name of the integration event.</param>
        /// <param name="source">The store the message comes from.</param>
        /// <param name="traceParent">The W3C trace context of the writer, or <see langword="null"/>.</param>
        public MessageMeasurement StartIntegrationEvent(string eventType, string source, string? traceParent)
        {
            Activity? activity = null;
            if (ActivitySource.HasListeners())
            {
                ActivityLink[]? links = ActivityContext.TryParse(traceParent, null, out ActivityContext writer)
                    ? [new ActivityLink(writer)]
                    : null;
                activity = ActivitySource.StartActivity(eventType, ActivityKind.Consumer, default(ActivityContext), links: links);
                activity?.SetTag(PhemeDiagnostics.OutboxSourceTagName, source);
            }

            return Measure(activity, eventType, PhemeDiagnostics.IntegrationEventKind);
        }

        private MessageMeasurement Measure(Activity? activity, string messageType, string messageKind)
        {
            if (activity is not null)
            {
                activity.SetTag(PhemeDiagnostics.MessageTypeTagName, messageType);
                activity.SetTag(PhemeDiagnostics.MessageKindTagName, messageKind);
            }

            return new MessageMeasurement(duration, activity, messageType, messageKind, Stopwatch.GetTimestamp());
        }

        public void Dispose()
        {
            ownMeter?.Dispose();
        }
    }
}
