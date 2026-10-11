using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Behaviors;
using Ordering.Infrastructure;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// The application's audit behavior: the ordering module's commands (<c>IAuditedCommand</c>) are recorded with their
    /// user and content, saved with the change they made, or not at all.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class AuditTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_command_is_recorded_with_its_user_and_content()
        {
            using HttpClient client = shop.Factory.CreateClient();
            string product = $"Kettle {Guid.NewGuid():N}";

            using HttpResponseMessage response = await client.PostOrder(new { product, quantity = 2 }, user: "alice");

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            AuditEntry entry = Assert.Single(await GetAuditTrail(product));
            Assert.Equal("alice", entry.User);
            Assert.Equal("PlaceOrderCommand", entry.Command);
            using JsonDocument content = JsonDocument.Parse(entry.Content);
            Assert.Equal(2, content.RootElement.GetProperty("Quantity").GetInt32());
        }

        [Fact]
        public async Task A_rejected_command_leaves_no_entry()
        {
            using HttpClient client = shop.Factory.CreateClient();
            string product = await shop.Factory.Services.CreateStock(available: 1);

            using HttpResponseMessage rejected = await client.PostOrder(new { product, quantity = 2 }, user: "alice");

            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            Assert.Empty(await GetAuditTrail(product));
        }

        // The entries of the orders of one product, told apart by the content.
        private async Task<List<AuditEntry>> GetAuditTrail(string product)
        {
            await using AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().AuditTrail
                .Where(e => e.Content.Contains(product))
                .ToListAsync(Token);
        }
    }
}
