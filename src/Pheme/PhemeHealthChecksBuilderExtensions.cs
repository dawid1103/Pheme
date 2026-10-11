using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Pheme;
using Pheme.Outbox;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Adds the health checks of Pheme to an <see cref="IHealthChecksBuilder"/>.
    /// </summary>
    public static class PhemeHealthChecksBuilderExtensions
    {
        /// <summary>
        /// The default name of the outbox health check: <c>pheme_outbox</c>.
        /// </summary>
        public const string OutboxCheckName = "pheme_outbox";

        /// <summary>
        /// Adds a health check of the outbox: <see cref="HealthStatus.Healthy"/>; <see cref="HealthStatus.Degraded"/>
        /// while the oldest pending message of a store occurred longer than <paramref name="maxPendingAge"/> ago, a
        /// message waiting for its next attempt included, when the delivery is stuck or behind; <paramref name="failureStatus"/> while a store has a parked message, which
        /// needs someone to fix its cause and requeue it (<see cref="IOutboxMaintenance"/>).
        /// </summary>
        /// <param name="builder">The health checks builder to add to.</param>
        /// <param name="maxPendingAge">How old a pending message may be before the outbox is degraded. The default is 5 minutes.</param>
        /// <param name="name">The name of the health check. The default is <see cref="OutboxCheckName"/>.</param>
        /// <param name="failureStatus">
        /// The status while a message is parked, and when the check fails, for example when a store cannot be read. The
        /// default is <see cref="HealthStatus.Unhealthy"/>.
        /// </param>
        /// <param name="tags">Tags to filter the health checks by, for example <c>outbox</c> for an endpoint of its own.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <remarks>
        /// The check reads <see cref="IOutboxStore.GetStats"/> of every store, resolved from the scope of the check run.
        /// Its data holds, for each store, <c>{source}.pending</c>, <c>{source}.parked</c> and, while a message is
        /// pending, <c>{source}.oldestPendingAge</c>. Requires the outbox (<c>AddOutbox</c> of
        /// <see cref="PhemeOptions"/>).
        /// <para>
        /// Map it to monitoring and alerts rather than to the readiness probe of the host: a parked message is delivered
        /// neither by a restart nor by taking the instance out of traffic. A check that has to be part of readiness
        /// passes <see cref="HealthStatus.Degraded"/> as <paramref name="failureStatus"/>, which ASP.NET Core answers
        /// with 200.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxPendingAge"/> is zero or negative.</exception>
        public static IHealthChecksBuilder AddPhemeOutboxCheck(
            this IHealthChecksBuilder builder,
            TimeSpan? maxPendingAge = null,
            string name = OutboxCheckName,
            HealthStatus? failureStatus = null,
            IEnumerable<string>? tags = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(name);
            TimeSpan age = maxPendingAge ?? TimeSpan.FromMinutes(5);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(age, TimeSpan.Zero, nameof(maxPendingAge));

            return builder.Add(new HealthCheckRegistration(
                name,
                serviceProvider => new OutboxHealthCheck(
                    serviceProvider.GetServices<IOutboxStore>(),
                    serviceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value,
                    serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System,
                    age),
                failureStatus,
                tags));
        }
    }
}
