# Operating the outbox

What an application needs once the outbox runs in production: handling parked messages, watching the delivery, and
changing integration events while messages of the previous version wait in the table. How the outbox delivers is in
[Integration events and the outbox](08-integration-events.md).

## Maintenance

`IOutboxMaintenance` (scoped) lists the parked messages of all stores with their errors and requeues
one or all of them, for an administration endpoint once the cause is fixed
([`OutboxController.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Controllers/OutboxController.cs)).
A requeued message starts again at once with no failed attempts and skips the handlers that completed. The processor
deletes processed messages older than `ProcessedRetention` (30 days by default, `null` keeps them) when it starts and
then every hour; pending and parked messages are never deleted.

Each deletion is logged for each store, at `Information` by default, in the category `Pheme.Outbox`: the outbox, how
many messages it deleted (also 0), the date before which they were deleted and when the next deletion is due:

```text
Deleted 12 processed messages older than 09/11/2026 11:00:00 +00:00 from outbox Ordering; the next deletion is due at 10/11/2026 12:00:00 +00:00
```

`OutboxOptions.RetentionLogLevel` sets the level, `None` turns the log off; a failed deletion is always an error.

## Health check

A stuck delivery or a parked message shows up in the outbox health check:

```csharp
builder.Services.AddHealthChecks()
    .AddPhemeOutboxCheck(maxPendingAge: TimeSpan.FromMinutes(5), tags: ["outbox"]);

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => !check.Tags.Contains("outbox") });
app.MapHealthChecks("/health/outbox", new HealthCheckOptions { Predicate = check => check.Tags.Contains("outbox") });
```

It is `Healthy`; `Degraded` while a store's oldest pending message occurred more than `maxPendingAge` ago (5 minutes by
default), a message waiting for its next attempt included; `Unhealthy` while a store has a parked message, or
`failureStatus` when given. Its data holds the pending and parked counts of each store and the age of its oldest
pending message. **Hook it to monitoring and alerts, not to the probes of the host:** a parked message needs a person,
and neither a restart nor taking the instance out of traffic delivers it - with one instance, a readiness probe would
take the whole API down for one message. If it has to be in readiness, pass `failureStatus: HealthStatus.Degraded`,
which ASP.NET Core answers with 200. The example maps it to `/health/outbox` only
([`Program.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Program.cs)).

## Changing an integration event

The outbox holds messages written by the previous version of the application,
pending or parked, and the new version reads them. The content is JSON with the web defaults (camel case, enums as
numbers), read into the event type of the stored name:

- Give each event an `[IntegrationEventName]` before its first deployment; without it the stored name is the full type
  name, which changes when the type moves.
- Additive changes are safe: a property missing from an old message reads as `null` or `default`, so add it nullable
  or with a default the handlers understand. In the example, `OrderPlacedIntegrationEvent.Express` was added later:
  an old message reads `false`, standard delivery, which is what every order was then
  ([`OrderPlacedIntegrationEvent.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Common/Contracts/OrderPlacedIntegrationEvent.cs)).
  A removed property is ignored.
- Renaming a property, changing its type or its meaning, or reordering the members of an enum breaks the old messages,
  silently or on every attempt. Give enum members explicit values. For a breaking change, write a new type with a new
  name (`ordering.order-placed.v2`) and keep the old type and its handlers until no unprocessed message has the old
  name: `SELECT count(*) FROM ordering.outbox_messages WHERE "Type" = 'ordering.order-placed' AND "ProcessedOnUtc" IS
  NULL` (the column names follow the naming convention of the context).
- Remove an event type, or its last handler, only then: a message whose name no handled type has fails with "No
  integration event handled by this application is named ..." until it is parked. If that happens after a
  deployment, bring the type and its handler back, deploy, and requeue the parked messages with
  `IOutboxMaintenance.RequeueAllParked()`.

---

Previous: [Integration events and the outbox](08-integration-events.md) · [Contents](../README.md) · Next: [Layers](10-layers.md)
