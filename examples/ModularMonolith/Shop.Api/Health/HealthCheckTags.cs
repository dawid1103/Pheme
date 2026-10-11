namespace Shop.Api.Health
{
    /// <summary>
    /// Tags of the application's health checks; the health check endpoints select the checks they run by them.
    /// </summary>
    internal static class HealthCheckTags
    {
        /// <summary>
        /// The outbox check, run only by the monitoring endpoint, <c>/health/outbox</c>: a parked message needs a person,
        /// and neither a restart nor taking the instance out of traffic delivers it, so the endpoint of the host's probes,
        /// <c>/health</c>, leaves it out.
        /// </summary>
        public const string Outbox = "outbox";
    }
}
