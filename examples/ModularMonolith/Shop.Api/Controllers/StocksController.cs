using Microsoft.AspNetCore.Mvc;
using Ordering.Application.Inventory.GetStock;
using Ordering.Application.Inventory.ReceiveStock;
using Pheme;

namespace Shop.Api.Controllers
{
    /// <summary>
    /// The stocks of the products, in the ordering module.
    /// </summary>
    [ApiController]
    [Route("stocks")]
    public sealed class StocksController(ISender sender) : ControllerBase
    {
        /// <summary>
        /// Reads the stock of a product: 200, possibly up to 30 seconds old, or 404 when the product has no stock.
        /// </summary>
        [HttpGet("{product}")]
        public Task<ProductStockDto> Get(string product, CancellationToken cancellationToken) =>
            sender.Send(new GetProductStockQuery(product), cancellationToken);

        /// <summary>
        /// Records a delivery of items, for the warehouse staff: 204, 400 when invalid, 403 for other users, 404 when the
        /// product has no stock.
        /// </summary>
        [HttpPost("{product}/receive")]
        public async Task<IActionResult> Receive(string product, ReceivedItems items, CancellationToken cancellationToken)
        {
            await sender.Send(new ReceiveStockCommand(product, items.Quantity), cancellationToken);
            return NoContent();
        }

        /// <summary>
        /// The body of <see cref="Receive"/>: the number of delivered items.
        /// </summary>
        public sealed record ReceivedItems(int Quantity);
    }
}
