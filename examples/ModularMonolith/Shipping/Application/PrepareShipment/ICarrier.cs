namespace Shipping.Application.PrepareShipment
{
    /// <summary>
    /// The carrier that collects the shipments from the warehouse; implemented by the infrastructure.
    /// </summary>
    public interface ICarrier
    {
        /// <summary>
        /// Books the pickup of the shipment of an order.
        /// </summary>
        Task SchedulePickup(Guid orderId, CancellationToken cancellationToken);
    }
}
