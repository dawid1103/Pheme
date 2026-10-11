using FluentValidation;

namespace Ordering.Application.Orders.PlaceOrder
{
    // Found by AddFluentValidation, like the handlers by AddPheme: no registration of its own.
    internal sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
    {
        public PlaceOrderCommandValidator()
        {
            RuleFor(command => command.Product).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Quantity).InclusiveBetween(1, 100);
        }
    }
}
