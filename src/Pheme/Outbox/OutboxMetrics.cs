using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace Pheme.Outbox
{
    /// <summary>
    /// The observable gauges of the outbox stores' state, read from <see cref="IOutboxStore.GetStats"/> only when a
    /// listener collects them.
    /// </summary>
    internal static class OutboxMetrics
    {
        public static void Observe(Meter meter, IServiceScopeFactory scopeFactory, OutboxOptions options, TimeProvider timeProvider)
        {
            meter.CreateObservableGauge(
                PhemeDiagnostics.OutboxMessagesName,
                () => ReadStats(scopeFactory, options.MaxAttempts).SelectMany(store => new[]
                {
                    new Measurement<int>(store.Stats.Pending, Source(store.Source), State(PhemeDiagnostics.OutboxPendingState)),
                    new Measurement<int>(store.Stats.Parked, Source(store.Source), State(PhemeDiagnostics.OutboxParkedState)),
                }),
                "{message}",
                "How many messages of an outbox store are pending or parked.");
            meter.CreateObservableGauge(
                PhemeDiagnostics.OutboxOldestPendingAgeName,
                () => ReadStats(scopeFactory, options.MaxAttempts).Select(store => new Measurement<double>(
                    store.Stats.OldestPendingOccurredOnUtc is { } oldest
                        ? Math.Max(0, (timeProvider.GetUtcNow() - oldest).TotalSeconds)
                        : 0,
                    Source(store.Source))),
                "s",
                "How long ago the oldest pending message of an outbox store occurred; 0 when none is pending.");
        }

        // Observable instruments are read synchronously, the stores asynchronously. A store that cannot be read, while
        // the database is down for example, is left out: collecting metrics must not fail.
        private static List<(string Source, OutboxStats Stats)> ReadStats(IServiceScopeFactory scopeFactory, int maxAttempts)
        {
            var stats = new List<(string Source, OutboxStats Stats)>();
            AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            try
            {
                foreach (IOutboxStore store in scope.ServiceProvider.GetServices<IOutboxStore>())
                {
                    try
                    {
                        stats.Add((store.Source, store.GetStats(maxAttempts, CancellationToken.None).GetAwaiter().GetResult()));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            finally
            {
                scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return stats;
        }

        private static KeyValuePair<string, object?> Source(string source)
        {
            return new KeyValuePair<string, object?>(PhemeDiagnostics.OutboxSourceTagName, source);
        }

        private static KeyValuePair<string, object?> State(string state)
        {
            return new KeyValuePair<string, object?>(PhemeDiagnostics.OutboxMessageStateTagName, state);
        }
    }
}
