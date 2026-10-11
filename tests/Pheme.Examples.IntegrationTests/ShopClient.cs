using System.Net;
using System.Net.Http.Json;
using Shop.Common.Security;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// The shop's endpoints as the tests call them. The warehouse endpoints are called by a user in the given roles,
    /// the warehouse staff by default, or anonymously when the roles are <see langword="null"/>.
    /// </summary>
    internal static class ShopClient
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        public static Task<HttpResponseMessage> PostOrder(this HttpClient client, object order) =>
            client.PostAsJsonAsync("/orders", order, Token);

        // Placed by the user named, or anonymously, once per idempotency key when one is given.
        public static Task<HttpResponseMessage> PostOrder(this HttpClient client, object order, string? user, Guid? idempotencyKey = null) =>
            client.Post("/orders", JsonContent.Create(order), [
                .. user is null ? [] : new[] { ("X-User", user) },
                .. idempotencyKey is null ? [] : new[] { ("Idempotency-Key", idempotencyKey.Value.ToString()) }]);

        public static async Task<Guid> PlaceOrder(this HttpClient client, string product = "Teapot", int quantity = 2, bool express = false)
        {
            using HttpResponseMessage response = await client.PostOrder(new { product, quantity, express });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            PlacedOrder? placed = await response.Content.ReadFromJsonAsync<PlacedOrder>(Token);
            return Assert.IsType<PlacedOrder>(placed).Id;
        }

        public static Task<HttpResponseMessage> CancelOrder(this HttpClient client, Guid id) =>
            client.PostAsync(new Uri($"/orders/{id}/cancel", UriKind.Relative), content: null, Token);

        public static Task<HttpResponseMessage> DispatchShipment(this HttpClient client, Guid orderId, string? roles = Roles.Warehouse) =>
            client.Post($"/shipments/{orderId}/dispatch", content: null, AsUserIn(roles));

        public static Task<HttpResponseMessage> ReceiveStock(this HttpClient client, string product, int quantity, string? roles = Roles.Warehouse) =>
            client.Post($"/stocks/{Uri.EscapeDataString(product)}/receive", JsonContent.Create(new { quantity }), AsUserIn(roles));

        public static Task<HttpResponseMessage> GetStock(this HttpClient client, string product) =>
            client.GetAsync(new Uri($"/stocks/{Uri.EscapeDataString(product)}", UriKind.Relative), Token);

        public static Task<HttpResponseMessage> GetOrder(this HttpClient client, Guid id) =>
            client.GetAsync(new Uri($"/orders/{id}", UriKind.Relative), Token);

        public static async Task<ParkedMessage[]> GetParkedOutboxMessages(this HttpClient client) =>
            Assert.IsType<ParkedMessage[]>(await client.GetFromJsonAsync<ParkedMessage[]>("/admin/outbox/parked", Token));

        public static Task<HttpResponseMessage> RequeueOutboxMessage(this HttpClient client, Guid id) =>
            client.PostAsync(new Uri($"/admin/outbox/{id}/requeue", UriKind.Relative), content: null, Token);

        // The status code and the status the health check endpoint writes: Healthy, Degraded or Unhealthy.
        public static async Task<(HttpStatusCode Code, string Status)> GetHealth(this HttpClient client, string path)
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        }

        // The headers of the example's stand-in authentication (HeaderAuthenticationHandler).
        private static (string Name, string Value)[] AsUserIn(string? roles) =>
            roles is null ? [] : [("X-User", "clerk"), ("X-Roles", roles)];

        private static async Task<HttpResponseMessage> Post(
            this HttpClient client, string path, HttpContent? content, (string Name, string Value)[] headers)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = content };
            foreach ((string name, string value) in headers)
            {
                request.Headers.Add(name, value);
            }

            return await client.SendAsync(request, Token);
        }

        public sealed record PlacedOrder(Guid Id);

        public sealed record Order(Guid Id, string Product, int Quantity, string Status);

        public sealed record Stock(string Product, int Available);

        public sealed record ParkedMessage(Guid Id, string Type, string Source, int AttemptCount, string Error, string? CompletedHandlers);
    }
}
