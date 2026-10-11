namespace Shipping.Domain
{
    /// <summary>
    /// An instruction for the warehouse staff, such as returning the items of a cancelled shipment to the shelves.
    /// </summary>
    public sealed class WarehouseNotice
    {
        private WarehouseNotice(Guid shipmentId, string instruction)
        {
            ShipmentId = shipmentId;
            Instruction = instruction;
        }

        /// <summary>
        /// The identifier of the notice.
        /// </summary>
        public Guid Id { get; private set; } = Guid.CreateVersion7();

        /// <summary>
        /// The identifier of the shipment the notice is about.
        /// </summary>
        public Guid ShipmentId { get; private set; }

        /// <summary>
        /// What the warehouse staff should do.
        /// </summary>
        public string Instruction { get; private set; }

        /// <summary>
        /// Creates a notice about a shipment.
        /// </summary>
        public static WarehouseNotice Create(Guid shipmentId, string instruction)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

            return new WarehouseNotice(shipmentId, instruction);
        }
    }
}
