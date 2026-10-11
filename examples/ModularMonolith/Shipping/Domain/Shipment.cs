using Shipping.Domain.Events;

namespace Shipping.Domain
{
    /// <summary>
    /// The shipment of one order: the aggregate of the shipping module.
    /// </summary>
    public sealed class Shipment : AggregateRoot
    {
        private Shipment(Guid orderId, bool express)
        {
            OrderId = orderId;
            Express = express;
        }

        /// <summary>
        /// The identifier of the shipped order, from the ordering module.
        /// </summary>
        public Guid OrderId { get; private set; }

        /// <summary>
        /// Whether the order is delivered by express.
        /// </summary>
        public bool Express { get; private set; }

        /// <summary>
        /// The state of the shipment.
        /// </summary>
        public ShipmentStatus Status { get; private set; }

        /// <summary>
        /// Tells whether the shipment may be dispatched: it is not cancelled.
        /// </summary>
        public bool CanDispatch => Status != ShipmentStatus.Cancelled;

        /// <summary>
        /// Starts preparing the shipment of an order.
        /// </summary>
        public static Shipment Prepare(Guid orderId, bool express) => new(orderId, express) { Status = ShipmentStatus.Preparing };

        /// <summary>
        /// Records the shipment of an order cancelled before its preparation started, so the order is never prepared
        /// later; raises nothing, as the warehouse has nothing to undo.
        /// </summary>
        public static Shipment CancelBeforePreparation(Guid orderId) => new(orderId, express: false) { Status = ShipmentStatus.Cancelled };

        /// <summary>
        /// Hands the shipment to the carrier and raises <see cref="ShipmentDispatchedDomainEvent"/>; dispatching a
        /// dispatched shipment changes nothing.
        /// </summary>
        /// <exception cref="InvalidOperationException">The shipment is cancelled (<see cref="CanDispatch"/>).</exception>
        public void Dispatch()
        {
            if (!CanDispatch)
            {
                throw new InvalidOperationException($"The shipment of order {OrderId} is cancelled and cannot be dispatched.");
            }

            if (Status != ShipmentStatus.Preparing)
            {
                return;
            }

            Status = ShipmentStatus.Dispatched;
            Raise(new ShipmentDispatchedDomainEvent(Id, OrderId));
        }

        /// <summary>
        /// Stops the shipment of a cancelled order. One being prepared is cancelled and raises
        /// <see cref="ShipmentPreparationCancelledDomainEvent"/>; one dispatched already cannot be undone, so the parcel is
        /// to come back and <see cref="ShipmentReturnRequestedDomainEvent"/> is raised. A cancelled shipment, or one whose
        /// return is requested, changes nothing.
        /// </summary>
        public void Cancel()
        {
            switch (Status)
            {
                case ShipmentStatus.Preparing:
                    Status = ShipmentStatus.Cancelled;
                    Raise(new ShipmentPreparationCancelledDomainEvent(Id, OrderId));
                    break;
                case ShipmentStatus.Dispatched:
                    Status = ShipmentStatus.ReturnRequested;
                    Raise(new ShipmentReturnRequestedDomainEvent(Id, OrderId));
                    break;
            }
        }
    }
}
