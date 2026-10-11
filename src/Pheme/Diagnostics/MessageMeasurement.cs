using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pheme.Internal;

namespace Pheme.Diagnostics
{
    /// <summary>
    /// The activity and the duration measurement of one message, started by <see cref="MessageInstrumentation"/>.
    /// </summary>
    internal readonly struct MessageMeasurement(
        Histogram<double> duration, Activity? activity, string messageType, string messageKind, long startTimestamp)
    {
        /// <summary>
        /// Records the duration and stops the activity, as a failure when <paramref name="exception"/> is given.
        /// Never throws.
        /// </summary>
        public void Stop(Exception? exception = null)
        {
            // Formatted only when someone listens: without a listener, a failure costs nothing extra.
            string? errorType = exception is not null && (duration.Enabled || activity is not null)
                ? TypeNames.Format(exception.GetType())
                : null;

            // A throwing listener is ignored: telemetry must never fail an operation that has already completed, maybe
            // committed, nor replace the exception it failed with.
            try
            {
                // Recorded before the activity stops, so an exemplar can point at it.
                if (duration.Enabled)
                {
                    var tags = new TagList
                    {
                        { PhemeDiagnostics.MessageTypeTagName, messageType },
                        { PhemeDiagnostics.MessageKindTagName, messageKind },
                    };
                    if (errorType is not null)
                    {
                        tags.Add(PhemeDiagnostics.ErrorTypeTagName, errorType);
                    }

                    duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
                }
            }
            catch (Exception)
            {
            }

            if (activity is null)
            {
                return;
            }

            try
            {
                if (errorType is not null)
                {
                    activity.SetTag(PhemeDiagnostics.ErrorTypeTagName, errorType);
                    activity.SetStatus(ActivityStatusCode.Error, exception!.Message);
                }

                activity.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
}
