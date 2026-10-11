using FluentValidation;
using FluentValidation.Results;
using Pheme;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Adds FluentValidation to the Pheme pipeline.
    /// </summary>
    public static class PhemeOptionsFluentValidationExtensions
    {
        /// <summary>
        /// Adds a pipeline behavior that validates every message with its <see cref="IValidator{T}"/> services before
        /// the handler runs and throws a <see cref="ValidationException"/> when validation fails, unless another call
        /// sets an exception factory.
        /// </summary>
        /// <param name="options">The options of the <c>AddPheme</c> call.</param>
        /// <returns>The same options, for chaining.</returns>
        /// <remarks>
        /// <para>
        /// Validators of the message type run one after another; their failures are thrown together in one exception,
        /// before the inner behaviors and the handler. The behavior takes its place in the pipeline like one added with
        /// <see cref="PhemeOptions.AddBehavior(Type)"/>.
        /// </para>
        /// <para>
        /// Validators are found in the assemblies of the handlers: every assembly added with
        /// <see cref="PhemeOptions.AddHandlersFromAssemblies(System.Reflection.Assembly[])"/>, also by other
        /// <c>AddPheme</c> calls, so keep them next to the handlers. They are added as transient services, including
        /// <see langword="internal"/> ones; a validator registered also by hand runs once. Several calls configure one
        /// behavior, with at most one exception factory.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The service collection contains Pheme registrations copied from another service collection.
        /// </exception>
        public static PhemeOptions AddFluentValidation(this PhemeOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            FluentValidationSettings.GetOrAdd(options.Services);
            return AddBehavior(options);
        }

        /// <summary>
        /// Adds a pipeline behavior that validates every message with its <see cref="IValidator{T}"/> services before
        /// the handler runs and throws the exception made by <paramref name="exceptionFactory"/> when validation fails.
        /// </summary>
        /// <param name="options">The options of the <c>AddPheme</c> call.</param>
        /// <param name="exceptionFactory">Makes the exception to throw from the failures of all validators.</param>
        /// <returns>The same options, for chaining.</returns>
        /// <remarks>
        /// Works like <see cref="AddFluentValidation(PhemeOptions)"/>; the factory applies to the validation of all
        /// messages. A factory returning <see langword="null"/> makes the send fail with an
        /// <see cref="InvalidOperationException"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="options"/> or <paramref name="exceptionFactory"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Another call has already set a different exception factory, or the service collection contains Pheme
        /// registrations copied from another service collection.
        /// </exception>
        public static PhemeOptions AddFluentValidation(
            this PhemeOptions options,
            Func<IReadOnlyList<ValidationFailure>, Exception> exceptionFactory)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(exceptionFactory);

            FluentValidationSettings.GetOrAdd(options.Services).SetExceptionFactory(exceptionFactory);
            return AddBehavior(options);
        }

        private static PhemeOptions AddBehavior(PhemeOptions options)
        {
            return options
                .AddBehavior(typeof(ValidationBehavior<,>))
                .AddAssemblyScanner(ValidatorScanner.Scan);
        }
    }
}
