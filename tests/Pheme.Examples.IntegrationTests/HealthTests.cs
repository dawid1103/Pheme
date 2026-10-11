using System.Net;

namespace Pheme.Examples.IntegrationTests
{
    [Collection(typeof(ShopCollection))]
    public class HealthTests(ShopFixture shop)
    {
        [Fact]
        public async Task Health_endpoint_reports_healthy_once_both_modules_reach_the_database()
        {
            using HttpClient client = shop.Factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }
}
