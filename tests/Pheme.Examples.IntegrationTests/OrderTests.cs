using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// Commands and queries of the ordering module, through <see cref="ISender"/> in the controller.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class OrderTests(ShopFixture shop)
    {
        private const string ProblemJson = "application/problem+json";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Placing_an_order_answers_its_identifier_and_location()
        {
            using HttpClient client = shop.Factory.CreateClient();

            using HttpResponseMessage response = await client.PostOrder(new { product = "Teapot", quantity = 3 });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            ShopClient.PlacedOrder? placed = await response.Content.ReadFromJsonAsync<ShopClient.PlacedOrder>(Token);
            Guid id = Assert.IsType<ShopClient.PlacedOrder>(placed).Id;
            Assert.Equal($"/orders/{id}", response.Headers.Location?.AbsolutePath);
            ShopClient.Order? order = await client.GetFromJsonAsync<ShopClient.Order>(response.Headers.Location, Token);
            Assert.Equal(new ShopClient.Order(id, "Teapot", 3, "Placed"), order);
        }

        [Fact]
        public async Task Placing_an_invalid_order_answers_400_with_the_validators_errors_per_field_and_saves_nothing()
        {
            using HttpClient client = shop.Factory.CreateClient();
            int ordersBefore = await CountOrders();

            using HttpResponseMessage response = await client.PostOrder(new { product = "", quantity = 0 });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
            HttpValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Token);
            Assert.NotNull(problem);
            Assert.Equal(["Product", "Quantity"], problem.Errors.Keys.Order());
            Assert.Equal(["'Product' must not be empty."], problem.Errors["Product"]);
            Assert.Equal(["'Quantity' must be between 1 and 100. You entered 0."], problem.Errors["Quantity"]);
            Assert.Equal(ordersBefore, await CountOrders());
        }

        [Fact]
        public async Task Placing_an_order_without_a_product_is_rejected_by_the_validator_not_by_MVC()
        {
            using HttpClient client = shop.Factory.CreateClient();

            using HttpResponseMessage response = await client.PostOrder(new { quantity = 1 });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            HttpValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Token);
            Assert.NotNull(problem);
            Assert.Equal(["'Product' must not be empty."], Assert.Single(problem.Errors).Value);
        }

        [Fact]
        public async Task Getting_a_missing_order_answers_404_with_the_handlers_message()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid id = Guid.CreateVersion7();

            using HttpResponseMessage response = await client.GetOrder(id);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
            Microsoft.AspNetCore.Mvc.ProblemDetails? problem =
                await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Token);
            Assert.Equal($"Order {id} does not exist.", problem?.Detail);
        }

        [Fact]
        public async Task Cancelling_an_order_answers_204_and_the_order_reads_cancelled()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid id = await client.PlaceOrder();

            using HttpResponseMessage response = await client.CancelOrder(id);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            ShopClient.Order? order = await client.GetFromJsonAsync<ShopClient.Order>($"/orders/{id}", Token);
            Assert.Equal("Cancelled", order?.Status);
        }

        [Fact]
        public async Task Cancelling_a_missing_order_answers_404()
        {
            using HttpClient client = shop.Factory.CreateClient();

            using HttpResponseMessage response = await client.CancelOrder(Guid.CreateVersion7());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        private async Task<int> CountOrders()
        {
            await using AsyncServiceScope scope = shop.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Orders.CountAsync(Token);
        }
    }
}
