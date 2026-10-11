using Microsoft.Extensions.Logging;
using Shipping.Application.PrepareShipment;

namespace Shipping.Infrastructure
{
    // The example has no real carrier: it logs the booking.
    internal sealed partial class LoggingCarrier(ILogger<LoggingCarrier> logger) : ICarrier
    {
        public Task SchedulePickup(Guid orderId, CancellationToken cancellationToken)
        {
            LogPickupScheduled(logger, orderId);
            return Task.CompletedTask;
        }

        [LoggerMessage(1, LogLevel.Information, "Pickup of the shipment of order {OrderId} scheduled")]
        private static partial void LogPickupScheduled(ILogger logger, Guid orderId);
    }
}
