using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Ordering.Application.Orders.GetOrder;
using Ordering.Application.Orders.PlaceOrder;
using Shop.Common.Errors;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// The pipeline the composition root builds: <c>AddRequestLogging</c>, the application's command-only behavior and
    /// <c>AddFluentValidation</c>.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class PipelineTests(ShopFixture shop)
    {
        private const string ServerTiming = "Server-Timing";

        [Fact]
        public async Task Validation_and_not_found_failures_are_logged_as_expected_without_a_stack_trace()
        {
            using HttpClient client = shop.Factory.CreateClient();
            shop.RequestLogs.Clear();

            using HttpResponseMessage invalid = await client.PostOrder(new { product = "Teapot", quantity = 0 });
            using HttpResponseMessage missing = await client.GetOrder(Guid.CreateVersion7());

            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Collection(
                shop.RequestLogs.GetSnapshot(),
                record => AssertExpectedFailure(record, typeof(PlaceOrderCommand), typeof(ValidationException)),
                record => AssertExpectedFailure(record, typeof(GetOrderQuery), typeof(NotFoundException)));
        }

        [Fact]
        public async Task Successes_are_not_logged_at_the_default_debug_level()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid id = await client.PlaceOrder();
            shop.RequestLogs.Clear();

            using HttpResponseMessage response = await client.GetOrder(id);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(shop.RequestLogs.GetSnapshot());
        }

        [Fact]
        public async Task A_command_with_a_response_is_timed()
        {
            using HttpClient client = shop.Factory.CreateClient();

            using HttpResponseMessage response = await client.PostOrder(new { product = "Teapot", quantity = 1 });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            string timing = Assert.Single(response.Headers.GetValues(ServerTiming));
            Assert.Matches("""^command;desc="PlaceOrderCommand";dur=\d+\.\d$""", timing);
        }

        [Fact]
        public async Task A_command_without_a_response_is_timed()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid id = await client.PlaceOrder();

            using HttpResponseMessage response = await client.CancelOrder(id);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            string timing = Assert.Single(response.Headers.GetValues(ServerTiming));
            Assert.Matches("""^command;desc="CancelOrderCommand";dur=\d+\.\d$""", timing);
        }

        [Fact]
        public async Task A_query_is_not_timed()
        {
            using HttpClient client = shop.Factory.CreateClient();
            Guid id = await client.PlaceOrder();

            using HttpResponseMessage response = await client.GetOrder(id);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(ServerTiming));
        }

        private static void AssertExpectedFailure(FakeLogRecord record, Type messageType, Type exceptionType)
        {
            Assert.Equal(LogLevel.Information, record.Level);
            Assert.Null(record.Exception);
            Assert.Equal(messageType.FullName, record.GetStructuredStateValue("MessageType"));
            Assert.Equal(exceptionType.FullName, record.GetStructuredStateValue("ExceptionType"));
        }
    }
}
