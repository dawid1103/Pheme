using Microsoft.AspNetCore.Mvc;
using Pheme;
using Shipping.Application.DispatchShipment;

namespace Shop.Api.Controllers
{
    /// <summary>
    /// The shipping module's endpoints, for the warehouse staff.
    /// </summary>
    [ApiController]
    [Route("shipments")]
    public sealed class ShipmentsController(ISender sender) : ControllerBase
    {
        /// <summary>
        /// Hands the shipment of an order to the carrier: 204, 403 for users outside the warehouse staff, 404 when the
        /// order has no shipment, 422 when the shipment is cancelled.
        /// </summary>
        [HttpPost("{orderId:guid}/dispatch")]
        public async Task<IActionResult> Dispatch(Guid orderId, CancellationToken cancellationToken)
        {
            await sender.Send(new DispatchShipmentCommand(orderId), cancellationToken);
            return NoContent();
        }
    }
}
