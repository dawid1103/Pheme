# Integration events and the outbox

The [example application](https://github.com/dawid1103/Pheme/blob/master/examples/README.md) writes and delivers
integration events, retries and parks failed ones, and has an administration endpoint and a health check for them.

An integration event (`IIntegrationEvent`) is written to the outbox table of its module in the same transaction as
the change that raised it, and delivered later, in the background, to its `IIntegrationEventHandler<TEvent>` handlers.
`AddOutbox` adds the delivery; handlers are scanned with the others:

```csharp
services.AddPheme(options => options
    .AddHandlersFromAssemblyContaining<OrderingModule>()
    .AddOutbox(outbox =>
    {
        outbox.PollingInterval = TimeSpan.FromSeconds(5);   // the defaults
        outbox.BatchSize = 20;
        outbox.MaxAttempts = 10;
        outbox.RetryDelay = TimeSpan.FromSeconds(10);       // doubled after each failed attempt...
        outbox.MaxRetryDelay = TimeSpan.FromMinutes(15);    // ...up to this
    }));
```

## Writing

Each `DbContext` that writes integration events maps the outbox table and names the modules writing to it;
a handler of the module adds events through `IOutboxWriter<TModule>`, addressed by a marker type of the module's
application layer, so it never sees the `DbContext`:

```csharp
// OnModelCreating of the module's DbContext
modelBuilder.HasDefaultSchema("ordering");
modelBuilder.AddPhemeOutbox();               // table outbox_messages in the default schema

// infrastructure of the module
services.AddPhemeDbContext<OrderingDbContext>(pheme => pheme
    .CollectDomainEventsFrom<IHasDomainEvents>(entity => entity.DomainEvents, entity => entity.ClearDomainEvents())
    .UseOutbox<OrderingModule>());           // IOutboxWriter<OrderingModule>, source "Ordering"

// application layer: public sealed class OrderingModule; - in a handler of OrderPlacedDomainEvent
outbox.Add(new OrderPlacedIntegrationEvent(Guid.CreateVersion7(), timeProvider.GetUtcNow(), domainEvent.OrderId, ...));
```

In the example, a domain event handler writes the integration event
([`WriteOrderPlacedToOutboxDomainEventHandler.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Ordering.Application/Orders/PlaceOrder/WriteOrderPlacedToOutboxDomainEventHandler.cs)),
and the shipping and ordering modules handle it once the order is saved. The modules work together only this way, in
both directions, each with an outbox of its own: a cancelled order stops its shipment, and a dispatched shipment marks
its order shipped. A module decides from its own data - ordering refuses to cancel a shipped order - and a change
crossing the other module's in flight is settled where it lands: a cancellation reaching a dispatched shipment asks for
the parcel back ([`Shipment.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shipping/Domain/Shipment.cs)).

`Add` puts the message into the context of the scope; the next `SaveChangesAsync` of that context writes it in the same
transaction as the change - or the save in progress, when a domain event handler adds it - and a save that fails or
never happens writes nothing. An event without a handler of its runtime type fails the start when its assembly is
scanned, and otherwise throws from `Add`: the writer and the handlers are in one application, and the outbox would
only park it. The message's `Id` is the event's `EventId`, so the same event cannot be stored twice; its content is the
JSON of the event's runtime type, its `Source` the context's name without `DbContext` (`OutboxSource` sets another).
The table belongs to Pheme: the column names follow the model, so a naming convention of the application applies to
them; create the table with the context's migrations. The outbox is supported on PostgreSQL only (see
[Limitations](14-limitations.md)).

## One outbox per `DbContext`

A context with `UseOutbox` gets one outbox store, which the outbox processor reads; the
modules only choose where their writer writes. So one API covers every layout:

```csharp
// modular monolith: a context per module, each with its own outbox table
services.AddPhemeDbContext<OrderingDbContext>(pheme => pheme.UseOutbox<OrderingModule>());
services.AddPhemeDbContext<ShippingDbContext>(pheme => pheme.UseOutbox<ShippingModule>());

// modules sharing one context: one table, one store, a writer per module
services.AddPhemeDbContext<SharedDbContext>(pheme => pheme.UseOutbox<CatalogModule>().UseOutbox<PricingModule>());

// an application without modules: any marker, for example the context itself
services.AddPhemeDbContext<AppDbContext>(pheme => pheme.UseOutbox<AppDbContext>());
```

A writer writes only to its own context: the message must be saved by the context and in the transaction of the
change. So a module belongs to one context, and each context's source must be unique - the same module or source in two
contexts fails the start. A store reads and updates only the messages of its source; it changes them with single
`UPDATE` and `DELETE` statements, so nothing else tracked by its context is saved with them.

## Delivery

The outbox processor, a hosted service, reads a batch of due messages from every outbox store, delivers them one by
one and saves each outcome:

- **At least once.** A message is processed when all its handlers have completed. A failed attempt records the
  error and the handlers that completed, and the next attempt calls only the others; a handler should still be
  idempotent, by `EventId` for example, because a crash between its work and the saved outcome repeats it.
- **A scope per handler.** Each handler gets a dependency injection scope of its own, and the outcome is saved in
  another new one, so what a failed handler left in its `DbContext` is never saved by someone else - not by the next
  handler of the same event either, even when both change the same module's `DbContext`.
- **Growing delay.** A failed message waits `RetryDelay` (10 seconds) before its next attempt, twice as long after each
  further failure, at most `MaxRetryDelay` (15 minutes); the time is stored with the message (`NextAttemptOnUtc`). An
  outage of what the handlers call, a mail server for example, is outlasted, and a message that keeps failing does not
  hold up the others.
- **Parking.** After `MaxAttempts` (10) failed attempts, about 50 minutes of failures with the default delays, a
  message is parked: no longer delivered, kept with its error. The failed attempts are logged as warnings with the
  delay before the next one, and parking as an error, in the category `Pheme.Outbox` (`OutboxOptions.CategoryName`).
- **Order and pace.** The due messages of a store are tried in the order they occurred (then of their `Id`, so events
  of one moment keep one order), but a failed message waits for its next attempt, so the ones after it may overtake
  it. After a full batch the processor goes on at once; otherwise it waits `PollingInterval`. A run that cannot read
  a store, while the database is down for example, is logged as an error and repeated after `PollingInterval`.
- **Exact type.** An event reaches the handlers of its runtime type only, as domain events do; the type is found by
  its stored name, `[IntegrationEventName("ordering.order-placed")]` or the full type name.

**One instance.** The stores do not lock the messages they read: run the processor in one instance of the
application only, or a message may be delivered twice at the same time.

For a schedule of your own, set `ProcessInBackground = false` and deliver with `IOutboxRunner.RunOnce()`, which
returns how many messages were processed, which failed, and whether more may be due.

**Integration tests.** `Pheme.Testing` turns the processor off and delivers at the points the test chooses, so a test
needs no polling: see [Testing](11-testing.md). Parked messages, the health check and changing an event type are in
[Operating the outbox](09-operating-the-outbox.md).

## Configuration errors

Configuration errors fail the start: integration event handlers without `AddOutbox`, `AddOutbox` without an outbox
store, `UseOutbox` without `AddOutbox`, the same module or source in two contexts, an integration event of the
scanned assemblies without a handler (with `AddOutbox`; for example its module was left out of the composition root),
and event types that cannot be delivered (an interface or abstract type, a generic type, an empty or duplicate stored
name). Add the assembly of the contracts to the scan, even without handlers, so its events are checked too. Without a
host, which validates on start, the errors of `UseOutbox` are thrown when a writer or a store is first resolved. A
context with `UseOutbox` whose model lacks `AddPhemeOutbox()`, or whose database the outbox does not support, fails the
first `Add` and the first use of its store.

---

Previous: [Domain events](07-domain-events.md) · [Contents](../README.md) · Next: [Operating the outbox](09-operating-the-outbox.md)
