# Testing

## Unit tests

Handlers are plain classes: a unit test creates one with its dependencies and calls `Handle`, without Pheme.

## Integration tests

Integration tests run the whole application against a real database, with `WebApplicationFactory<Program>` for an
API, and send it requests, so the pipeline, the domain events and the save are tested together. `Pheme.Testing` makes
the outbox predictable. Step by step, as the example's tests do:

1. **Deliver only when the test says so.** `UsePhemeOutboxForTests` turns the outbox processor off and sets
   `RetryDelay` to zero, so each drain retries a failed message at once
   ([`ShopFixture.cs`](https://github.com/dawid1103/Pheme/blob/master/tests/Pheme.Examples.IntegrationTests/ShopFixture.cs)):

   ```csharp
   new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
       .UseSetting("ConnectionStrings:Shop", database.GetConnectionString())   // Testcontainers
       .ConfigureTestServices(services => services.UsePhemeOutboxForTests()));
   ```

2. **Send, drain, assert.** The change is saved when the request answers; its integration events are delivered when
   the test drains the outbox, events written by the handlers included
   ([`OutboxTests.cs`](https://github.com/dawid1103/Pheme/blob/master/tests/Pheme.Examples.IntegrationTests/OutboxTests.cs)):

   ```csharp
   Guid orderId = await client.PlaceOrder();
   Assert.Single(await factory.Services.GetOutboxEvents<OrderPlacedIntegrationEvent>(), e => e.OrderId == orderId);

   await factory.Services.DrainOutbox();
   Assert.Equal(ShipmentStatus.Preparing, (await FindShipment(orderId))?.Status);
   ```

3. **Deliver again: is the handler idempotent?**

   ```csharp
   await factory.Services.Redeliver<OrderCancelledIntegrationEvent>(e => e.OrderId == orderId);
   await factory.Services.DrainOutbox();   // all handlers once more
   Assert.Single(await GetWarehouseNotices(shipment.Id));
   ```

4. **Failures.** A drain throws for a failed message, with its error, so a broken handler fails the test that caused
   it. A test of retries makes a fake fail on demand and drains once per attempt
   ([`OutboxMaintenanceTests.cs`](https://github.com/dawid1103/Pheme/blob/master/tests/Pheme.Examples.IntegrationTests/OutboxMaintenanceTests.cs));
   tests that count attempts or park messages run an instance of the application on a database of their own, so the
   messages of other tests do not interfere.

5. **Architecture tests** keep each layer to what it may use of Pheme, with `PhemeLayerRule`: see [Layers](10-layers.md).

## The helpers

The helpers of `Pheme.Testing` for the outbox, extension methods of `IServiceProvider`:

- `DrainOutbox(maxRounds = 10)` runs `RunOnce` until a run delivers nothing, and throws `InvalidOperationException`
  for a failed message with its type, identifier, store and error. Each failed message is reported once: tests sharing
  the application share its outbox, and a message that failed in one test, retried by the next drains, does not fail
  the others. A chain still delivering after `maxRounds` runs throws too, and so does a drain without
  `UsePhemeOutboxForTests`, as the processor would deliver the messages and their failures would go unnoticed.
- `GetOutboxEvents<TEvent>(source = null)` reads the events of exactly `TEvent` saved in the outbox - pending, parked
  and processed - in the order they occurred.
- `Redeliver<TEvent>(predicate)` makes the matching processed events pending again with no completed handlers, so the
  next drain calls all their handlers.

---

Previous: [Layers](10-layers.md) · [Contents](../README.md) · Next: [Tracing and metrics](12-tracing-and-metrics.md)
