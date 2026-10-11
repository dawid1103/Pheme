using FluentValidation;

namespace Ordering.Application.Inventory.ReceiveStock
{
    internal sealed class ReceiveStockCommandValidator : AbstractValidator<ReceiveStockCommand>
    {
        public ReceiveStockCommandValidator()
        {
            RuleFor(command => command.Product).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Quantity).InclusiveBetween(1, 10_000);
        }
    }
}
