using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Pheme
{
    /// <summary>
    /// The validation settings of all <c>AddFluentValidation</c> calls on one service collection, stored in the
    /// collection itself.
    /// </summary>
    internal sealed class FluentValidationSettings
    {
        private readonly IServiceCollection services;
        private Func<IReadOnlyList<ValidationFailure>, Exception>? exceptionFactory;

        private FluentValidationSettings(IServiceCollection services)
        {
            this.services = services;
        }

        /// <exception cref="InvalidOperationException">The instance in <paramref name="services"/> was created for another collection.</exception>
        public static FluentValidationSettings GetOrAdd(IServiceCollection services)
        {
            ServiceDescriptor? existing = services.FirstOrDefault(
                descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(FluentValidationSettings));
            if (existing?.ImplementationInstance is FluentValidationSettings settings)
            {
                return ReferenceEquals(settings.services, services)
                    ? settings
                    : throw new InvalidOperationException(
                        "This service collection contains Pheme registrations copied from another service collection. "
                        + "Call AddPheme on the final service collection, and do not copy Pheme registrations between "
                        + "collections.");
            }

            settings = new FluentValidationSettings(services);
            services.AddSingleton(settings);
            return settings;
        }

        /// <summary>
        /// Sets the exception factory; setting it again to an equal one is allowed.
        /// </summary>
        /// <exception cref="InvalidOperationException">A different factory is already set.</exception>
        public void SetExceptionFactory(Func<IReadOnlyList<ValidationFailure>, Exception> factory)
        {
            if (factory.Equals(exceptionFactory))
            {
                return;
            }

            if (exceptionFactory is not null)
            {
                throw new InvalidOperationException(
                    "FluentValidation is already configured with a different exception factory. Validation is one "
                    + "behavior for all messages, so it has one exception factory: pass it to one AddFluentValidation "
                    + "call only.");
            }

            exceptionFactory = factory;
        }

        public Exception CreateException(IReadOnlyList<ValidationFailure> failures)
        {
            if (exceptionFactory is null)
            {
                return new ValidationException(failures);
            }

            return exceptionFactory(failures)
                ?? throw new InvalidOperationException(
                    "The exception factory passed to AddFluentValidation returned null instead of an exception.");
        }
    }
}
