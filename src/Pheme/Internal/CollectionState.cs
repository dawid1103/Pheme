using Microsoft.Extensions.DependencyInjection;

namespace Pheme.Internal
{
    /// <summary>
    /// Finds Pheme state stored in a service collection as a singleton instance, created for that collection.
    /// </summary>
    internal static class CollectionState
    {
        /// <summary>
        /// Gets the <typeparamref name="TState"/> instance registered in <paramref name="services"/>, or
        /// <see langword="null"/> when there is none.
        /// </summary>
        /// <param name="services">The collection to search.</param>
        /// <param name="owner">Gets the collection an instance was created for.</param>
        /// <exception cref="InvalidOperationException">
        /// The instance was created for another collection, so the registrations were copied from it.
        /// </exception>
        public static TState? Find<TState>(IServiceCollection services, Func<TState, IServiceCollection> owner)
            where TState : class
        {
            ServiceDescriptor? existing = services.FirstOrDefault(
                descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(TState));
            if (existing?.ImplementationInstance is not TState state)
            {
                return null;
            }

            return ReferenceEquals(owner(state), services)
                ? state
                : throw new InvalidOperationException(
                    "This service collection contains Pheme registrations copied from another service collection. Call "
                    + "AddPheme on the final service collection, and do not copy Pheme registrations between collections.");
        }
    }
}
