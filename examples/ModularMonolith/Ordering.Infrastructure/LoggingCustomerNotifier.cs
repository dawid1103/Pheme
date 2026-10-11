using Microsoft.Extensions.Logging;
using Ordering.Application.Orders.Confirmation;

namespace Ordering.Infrastructure
{
    // The example sends no e-mails: it logs the confirmation.
    internal sealed partial class LoggingCustomerNotifier(ILogger<LoggingCustomerNotifier> logger) : ICustomerNotifier
    {
        public Task SendOrderConfirmation(Guid orderId, string product, int quantity, CancellationToken cancellationToken)
        {
            LogOrderConfirmed(logger, orderId, quantity, product);
            return Task.CompletedTask;
        }

        [LoggerMessage(1, LogLevel.Information, "Order {OrderId} of {Quantity} x {Product} confirmed to the customer")]
        private static partial void LogOrderConfirmed(ILogger logger, Guid orderId, int quantity, string product);
    }
}
