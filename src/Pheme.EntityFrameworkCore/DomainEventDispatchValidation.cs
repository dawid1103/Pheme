using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pheme
{
    /// <summary>
    /// Checks that <typeparamref name="TContext"/>, which publishes domain events, gets its options in the scope it is
    /// created in: its <see cref="DbContextOptions{TContext}"/> and its <see cref="IDbContextFactory{TContext}"/>, if
    /// any, are scoped in the final service collection.
    /// </summary>
    /// <remarks>
    /// Runs on start, and without a host when the options of the context are first built. Options built outside a
    /// scope get the dispatch interceptor from the root service provider: with scope validation (Development) the
    /// container throws about the internal interceptor, without it (Production) the handlers silently get the root's
    /// services.
    /// </remarks>
    internal sealed class DomainEventDispatchValidation<TContext>(IServiceCollection services)
        : IValidateOptions<DomainEventDispatchValidationOptions<TContext>>
        where TContext : DbContext
    {
        public ValidateOptionsResult Validate(string? name, DomainEventDispatchValidationOptions<TContext> options)
        {
            // The last registration of a service is the one the container resolves.
            ServiceDescriptor? unscoped = new[] { typeof(DbContextOptions<TContext>), typeof(IDbContextFactory<TContext>) }
                .Select(serviceType => services.LastOrDefault(descriptor => descriptor.ServiceType == serviceType))
                .FirstOrDefault(descriptor => descriptor is not null && descriptor.Lifetime != ServiceLifetime.Scoped);
            if (unscoped is null)
            {
                return ValidateOptionsResult.Success;
            }

            string context = typeof(TContext).Name;
            string service = unscoped.ServiceType == typeof(DbContextOptions<TContext>) ? nameof(DbContextOptions<>) : nameof(IDbContextFactory<>);
            return ValidateOptionsResult.Fail(
                $"{context} publishes domain events (CollectDomainEventsFrom in AddPhemeDbContext) through the scope it is "
                + $"created in, but its {service}<{context}> is {unscoped.Lifetime.ToString().ToLowerInvariant()}, so it "
                + "would publish them through the root service provider. Register the context with AddDbContext and its "
                + "default scoped options; AddDbContextPool and AddPooledDbContextFactory are not supported, and "
                + "AddDbContextFactory needs ServiceLifetime.Scoped.");
        }
    }
}
