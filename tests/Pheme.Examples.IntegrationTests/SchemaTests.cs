using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure;
using Shipping.Infrastructure;

namespace Pheme.Examples.IntegrationTests
{
    [Collection(typeof(ShopCollection))]
    public class SchemaTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Each_module_gets_its_tables_in_the_test_database()
        {
            await using AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope();
            OrderingDbContext ordering = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            ShippingDbContext shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();

            Assert.Equal(shop.ConnectionString, ordering.Database.GetConnectionString());
            Assert.Equal(shop.ConnectionString, shipping.Database.GetConnectionString());

            // A query of a missing table throws; other tests of the collection may have added rows already.
            await ordering.Orders.CountAsync(Token);
            await shipping.Shipments.CountAsync(Token);
        }
    }
}
