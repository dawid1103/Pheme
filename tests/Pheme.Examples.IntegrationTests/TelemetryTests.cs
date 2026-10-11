using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Ordering.Application.Orders.PlaceOrder;
using Ordering.Domain.Orders.Events;

namespace Pheme.Examples.IntegrationTests
{
    /// <summary>
    /// Pheme's spans and metrics in the application's OpenTelemetry exporter, here an in-memory one: the span of a
    /// command inside the trace of its HTTP request, the spans of its domain events and database calls inside it, and the
    /// delivery of the outbox in a trace of its own, linked to the request's.
    /// </summary>
    [Collection(typeof(ShopCollection))]
    public class TelemetryTests(ShopFixture shop)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_request_records_the_spans_and_metrics_of_its_command_events_and_outbox_delivery()
        {
            List<Activity> spans = [];
            List<Metric> metrics = [];
            await using WebApplicationFactory<Program> application = shop.CreateFactoryWithOwnDatabase()
                .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
                    .ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(spans))
                    .ConfigureOpenTelemetryMeterProvider(meters => meters.AddInMemoryExporter(metrics))));
            using HttpClient client = application.CreateClient();

            // The trace the request continues, as a caller instrumented with OpenTelemetry would send it.
            var traceId = ActivityTraceId.CreateRandom();
            client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
            await client.PlaceOrder();
            await application.Services.DrainOutbox(cancellationToken: Token);
            application.Services.GetRequiredService<TracerProvider>().ForceFlush();
            application.Services.GetRequiredService<MeterProvider>().ForceFlush();

            Activity command = Assert.Single(spans, span => span.DisplayName == typeof(PlaceOrderCommand).FullName);
            Assert.Equal(traceId, command.TraceId);
            Assert.Equal(PhemeDiagnostics.CommandKind, command.GetTagItem(PhemeDiagnostics.MessageKindTagName));
            Activity domainEvent = Assert.Single(spans, span => span.DisplayName == typeof(OrderPlacedDomainEvent).FullName);
            Assert.Equal(command.SpanId, domainEvent.ParentSpanId);
            Assert.Contains(spans, span => span.Source.Name == "Npgsql" && span.ParentSpanId == command.SpanId);

            Activity delivery = Assert.Single(spans, span => span.DisplayName == "ordering.order-placed");
            Assert.Equal(ActivityKind.Consumer, delivery.Kind);
            Assert.NotEqual(traceId, delivery.TraceId);
            Assert.Contains(delivery.Links, link => link.Context.TraceId == traceId);

            Assert.Contains(metrics, metric => metric.Name == PhemeDiagnostics.MessageDurationName);
            Assert.Contains(metrics, metric => metric.Name == PhemeDiagnostics.OutboxMessagesName);
        }
    }
}
