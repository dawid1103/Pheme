using Microsoft.Extensions.DependencyInjection.Extensions;
using Pheme;
using Pheme.Testing;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Prepares the outbox of Pheme for integration tests.
    /// </summary>
    public static class PhemeTestingServiceCollectionExtensions
    {
        /// <summary>
        /// Turns the outbox processor off, so the messages are delivered only when the test calls
        /// <see cref="PhemeTestingServiceProviderExtensions.DrainOutbox"/>, which reports their failures, and makes a
        /// failed message due again at once, so the next <c>DrainOutbox</c> retries it.
        /// </summary>
        /// <param name="services">The services of the application under test.</param>
        /// <returns>The same service collection, for chaining.</returns>
        /// <remarks>
        /// Sets <see cref="OutboxOptions.ProcessInBackground"/> to <see langword="false"/> and
        /// <see cref="OutboxOptions.RetryDelay"/> to <see cref="TimeSpan.Zero"/> after all other configuration, so it may
        /// be called before or after <c>AddOutbox</c>, for example in <c>ConfigureTestServices</c> of a
        /// <c>WebApplicationFactory</c>. Without the processor, processed messages are not deleted either.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        public static IServiceCollection UsePhemeOutboxForTests(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<OutboxDrainState>();
            services.PostConfigure<OutboxOptions>(outbox =>
            {
                outbox.ProcessInBackground = false;
                outbox.RetryDelay = TimeSpan.Zero;
            });
            return services;
        }
    }
}
