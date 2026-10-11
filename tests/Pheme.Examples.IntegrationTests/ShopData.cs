using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Inventory;
using Ordering.Infrastructure;
using Shipping.Domain;
using Shipping.Infrastructure;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// Data the shop has no endpoint for, saved and read directly.
    /// </summary>
    internal static class ShopData
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        /// Starts counting the items of a product, a new one unless <paramref name="product"/> is given; returns its name.
        /// </summary>
        public static async Task<string> CreateStock(this IServiceProvider services, int available, int reorderLevel = 0, string? product = null)
        {
            product ??= $"Kettle {Guid.NewGuid():N}";
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            OrderingDbContext ordering = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            ordering.ProductStocks.Add(ProductStock.Create(product, available, reorderLevel));
            await ordering.SaveChangesAsync(Token);
            return product;
        }

        /// <summary>
        /// Gets the history of an order in the order it was written.
        /// </summary>
        public static async Task<(int Sequence, string Description)[]> GetOrderHistory(this IServiceProvider services, Guid orderId)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return [.. (await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().OrderHistory
                .Where(e => e.OrderId == orderId)
                .OrderBy(e => e.Sequence)
                .ToArrayAsync(Token))
                .Select(e => (e.Sequence, e.Description))];
        }

        /// <summary>
        /// Finds the shipment of an order; <see langword="null"/> until the shipping module has prepared it.
        /// </summary>
        public static async Task<Shipment?> FindShipment(this IServiceProvider services, Guid orderId)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ShippingDbContext>().Shipments
                .SingleOrDefaultAsync(s => s.OrderId == orderId, Token);
        }

        /// <summary>
        /// Gets the instructions of the warehouse notices of a shipment.
        /// </summary>
        public static async Task<string[]> GetWarehouseNotices(this IServiceProvider services, Guid shipmentId)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ShippingDbContext>().WarehouseNotices
                .Where(n => n.ShipmentId == shipmentId)
                .Select(n => n.Instruction)
                .ToArrayAsync(Token);
        }
    }
}
