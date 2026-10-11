namespace Shipping.Domain
{
    /// <summary>
    /// The state of a <see cref="Shipment"/>.
    /// </summary>
    public enum ShipmentStatus
    {
        /// <summary>
        /// Being prepared for dispatch.
        /// </summary>
        Preparing,

        /// <summary>
        /// Preparation cancelled together with the order.
        /// </summary>
        Cancelled,

        /// <summary>
        /// Handed to the carrier.
        /// </summary>
        Dispatched,

        /// <summary>
        /// Dispatched, but its order was cancelled meanwhile: the parcel is to come back from the carrier.
        /// </summary>
        ReturnRequested,
    }
}
