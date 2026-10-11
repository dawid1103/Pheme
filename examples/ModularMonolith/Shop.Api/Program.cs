using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Ordering.Application.Behaviors;
using Ordering.Infrastructure;
using Pheme;
using Shipping.Infrastructure;
using Shop.Api.Behaviors;
using Shop.Api.Errors;
using Shop.Api.Health;
using Shop.Api.Security;
using Shop.Common.Contracts;
using Shop.Common.Errors;
using Shop.Common.Persistence;
using Shop.Common.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string connectionString = builder.Configuration.GetConnectionString("Shop")
    ?? throw new InvalidOperationException(
        "The connection string 'Shop' is not configured; set ConnectionStrings__Shop to a PostgreSQL database.");

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiExceptionFilter>();

    // Validation is the validators' job: without this, MVC answers 400 itself for a missing non-nullable string.
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});
builder.Services.AddHttpContextAccessor();

// A stand-in for real authentication (JWT bearer tokens, cookies): the user and roles come from request headers. The
// handlers see the user through ICurrentUser, so swapping the scheme changes nothing else.
builder.Services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, configureOptions: null);
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// In memory here; with a distributed cache registered (Redis, for example), HybridCache uses it as a second level.
builder.Services.AddHybridCache();

// Each module registers its own handlers (AddPheme with its assembly); the composition root adds the pipeline, the
// outbox and the contracts shared by the modules.
builder.Services.AddOrdering(connectionString);
builder.Services.AddShipping(connectionString);
builder.Services.AddPheme(options =>
{
    // The integration events the modules exchange: without a handler, because its module is left out, an event fails
    // the start instead of the command that writes it.
    options.AddHandlersFromAssemblyContaining<OrderPlacedIntegrationEvent>();

    // Delivers the integration events of both modules' outboxes in the background, with the defaults unless the
    // configuration section "Outbox" sets them (PollingInterval, BatchSize, MaxAttempts, ...).
    options.AddOutbox(outbox => builder.Configuration.GetSection("Outbox").Bind(outbox));

    // Always the outermost behavior: logs each message with the outcome of the rest of the pipeline. The exceptions
    // ApiExceptionFilter answers with 4xx are outcomes the application expects, not errors.
    options.AddRequestLogging(logging => logging.IsExpected = exception =>
        exception is ValidationException or ForbiddenException or NotFoundException or BusinessRuleException
            || ApiExceptionFilter.IsConflict(exception));

    // The application's own behaviors, in order: outermost first. Not part of Pheme. Authorization first, so a
    // message the user may not send is neither timed nor validated.
    options.AddBehavior(typeof(AuthorizationBehavior<,>));

    // Adds the time of each command to the response as a Server-Timing header.
    options.AddBehavior(typeof(CommandTimingBehavior<>));
    options.AddBehavior(typeof(CommandTimingBehavior<,>));

    // Validators of the module assemblies are found by the scan; failures become the application's exception.
    options.AddFluentValidation(failures => new ValidationException(failures
        .GroupBy(failure => failure.PropertyName)
        .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray())));

    // Valid queries that may be served from the cache for a while.
    options.AddBehavior(typeof(QueryCachingBehavior<,>));

    // The ordering module's commands: handled once per idempotency key; recorded in the audit trail, saved with their
    // change; innermost, around the handler only, a transaction for the commands that ask for one.
    options.AddBehavior(typeof(IdempotencyBehavior<,>));
    options.AddBehavior(typeof(AuditBehavior<,>));
    options.AddBehavior(typeof(TransactionBehavior<>));
});

// Traces and metrics: Pheme's span of every message, domain event and outbox delivery, inside the span of the HTTP
// request and around the database calls, and Pheme's metrics. Exported over OTLP - to the Aspire dashboard, Jaeger or an
// OpenTelemetry collector - when OTEL_EXPORTER_OTLP_ENDPOINT is set.
IOpenTelemetryBuilder telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Shop.Api"))
    .WithTracing(tracing => tracing
        .AddSource(PhemeDiagnostics.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddNpgsql())
    .WithMetrics(metrics => metrics
        .AddMeter(PhemeDiagnostics.MeterName)
        .AddAspNetCoreInstrumentation());
if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    telemetry.UseOtlpExporter();
}

// GET /health checks the modules' databases, for the probes of the host. GET /health/outbox checks only the outbox,
// for monitoring and alerts: degraded while a message waits longer than 5 minutes, unhealthy while a message is
// parked. A parked message needs a person; neither a restart nor taking the instance out of traffic delivers it, so
// the probes leave the outbox out.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderingDbContext>()
    .AddDbContextCheck<ShippingDbContext>()
    .AddPhemeOutboxCheck(maxPendingAge: TimeSpan.FromMinutes(5), tags: [HealthCheckTags.Outbox]);

WebApplication app = builder.Build();

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().EnsureSchemaCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<ShippingDbContext>().EnsureSchemaCreatedAsync();
}

app.UseAuthentication();
app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => !check.Tags.Contains(HealthCheckTags.Outbox) });
app.MapHealthChecks("/health/outbox", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthCheckTags.Outbox) });

await app.RunAsync();
