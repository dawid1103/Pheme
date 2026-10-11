using Microsoft.AspNetCore.Mvc;
using Ordering.Application.Orders.CancelOrder;
using Ordering.Application.Orders.GetOrder;
using Ordering.Application.Orders.PlaceOrder;
using Pheme;

namespace Shop.Api.Controllers
{
    /// <summary>
    /// The ordering module's endpoints: each one sends a single message.
    /// </summary>
    [ApiController]
    [Route("orders")]
    public sealed class OrdersController(ISender sender) : ControllerBase
    {
        /// <summary>
        /// Places an order: 201 with its identifier, 400 when the command is invalid. Sent again with the same
        /// <c>Idempotency-Key</c> header, it answers the same identifier and places nothing more.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Place(
            PlaceOrderCommand command,
            [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey,
            CancellationToken cancellationToken)
        {
            Guid id = await sender.Send(command with { IdempotencyKey = idempotencyKey }, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id }, new { id });
        }

        /// <summary>
        /// Cancels an order: 204, or 404 when it does not exist.
        /// </summary>
        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
        {
            await sender.Send(new CancelOrderCommand(id), cancellationToken);
            return NoContent();
        }

        /// <summary>
        /// Reads an order: 200, or 404 when it does not exist.
        /// </summary>
        [HttpGet("{id:guid}")]
        public Task<OrderDto> Get(Guid id, CancellationToken cancellationToken) =>
            sender.Send(new GetOrderQuery(id), cancellationToken);
    }
}
