using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pheme.Outbox
{
    /// <summary>
    /// Reports the failure status of its registration while a message is parked, and degraded while the oldest pending
    /// message is older than <paramref name="maxPendingAge"/>.
    /// </summary>
    internal sealed class OutboxHealthCheck(
        IEnumerable<IOutboxStore> stores, OutboxOptions options, TimeProvider timeProvider, TimeSpan maxPendingAge) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            var data = new Dictionary<string, object>();
            var parked = new List<string>();
            var late = new List<string>();
            foreach (IOutboxStore store in stores)
            {
                OutboxStats stats = await store.GetStats(options.MaxAttempts, cancellationToken);
                data[$"{store.Source}.pending"] = stats.Pending;
                data[$"{store.Source}.parked"] = stats.Parked;
                if (stats.Parked > 0)
                {
                    parked.Add($"{store.Source} has {stats.Parked} parked {(stats.Parked == 1 ? "message" : "messages")}");
                }

                if (stats.OldestPendingOccurredOnUtc is { } oldest)
                {
                    TimeSpan age = now - oldest;
                    data[$"{store.Source}.oldestPendingAge"] = age;
                    if (age > maxPendingAge)
                    {
                        late.Add($"{store.Source} has a message pending for {age:c}");
                    }
                }
            }

            return parked.Count > 0
                ? new HealthCheckResult(context.Registration.FailureStatus, $"Outbox: {string.Join("; ", [.. parked, .. late])}.", data: data)
                : late.Count > 0
                    ? HealthCheckResult.Degraded($"Outbox: {string.Join("; ", late)}, longer than {maxPendingAge:c}.", data: data)
                    : HealthCheckResult.Healthy(data: data);
        }
    }
}
