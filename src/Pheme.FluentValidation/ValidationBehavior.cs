using FluentValidation;
using FluentValidation.Results;

namespace Pheme
{
    /// <summary>
    /// Validates a message with all its <see cref="IValidator{T}"/> services before the inner behaviors and the
    /// handler run, and throws the exception made by <see cref="FluentValidationSettings"/> when any of them fails.
    /// </summary>
    /// <remarks>
    /// Validators run one after another, so they may share a scoped <c>DbContext</c>. A validator type registered more
    /// than once runs once.
    /// </remarks>
    internal sealed class ValidationBehavior<TMessage, TResponse>(
        IEnumerable<IValidator<TMessage>> validators,
        FluentValidationSettings settings)
        : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public async Task<TResponse> Handle(
            TMessage message,
            MessageHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            HashSet<Type>? validatorTypes = null;
            List<ValidationFailure>? failures = null;
            foreach (IValidator<TMessage> validator in validators)
            {
                if (!(validatorTypes ??= []).Add(validator.GetType()))
                {
                    continue;
                }

                // A new context per validator: a shared one accumulates the failures of the previous validators.
                ValidationResult result = await validator.ValidateAsync(message, cancellationToken);
                if (!result.IsValid)
                {
                    (failures ??= []).AddRange(result.Errors);
                }
            }

            if (failures is not null)
            {
                throw settings.CreateException(failures);
            }

            return await next(cancellationToken);
        }
    }
}
