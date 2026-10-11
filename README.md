# Pheme

[![CI](https://github.com/dawid1103/Pheme/actions/workflows/ci.yml/badge.svg)](https://github.com/dawid1103/Pheme/actions/workflows/ci.yml)

Lightweight in-process messaging for .NET: commands, queries, pipeline behaviors, domain events and integration
events through an outbox.

*Messages travel. Pheme carries the word.*

![Pheme: commands and queries, domain events, integration events and the outbox](https://raw.githubusercontent.com/dawid1103/Pheme/master/docs/assets/pheme-overview.jpg)

- **Commands and queries** with exactly one handler each, sent through `ISender` and wrapped in an ordered
  pipeline of behaviors. Missing and duplicate handlers fail the start of the application, not the first request.
- **Ready-made behaviors**: FluentValidation and request logging, configured rather than written.
- **Domain events** published in-process to any number of handlers, in order, before the save writes, so their
  changes are saved in its transaction; with Entity Framework Core, on `SaveChangesAsync` without a base class.
- **Integration events through an outbox**, written in the transaction of the change and delivered after it is saved,
  at least once, with retries, parking, a health check and helpers for integration tests.
- **Tracing and metrics** through `System.Diagnostics`, for OpenTelemetry.
- No reflection per call, no source generators, `internal` handlers welcome, MIT licence. Targets .NET 10.

## Packages

| Package | Contents | Used by |
|---|---|---|
| `Pheme.Abstractions` | message, handler and behavior contracts; no dependencies | application layer |
| `Pheme` | `AddPheme`, sender, pipeline, request logging, domain event publisher, outbox processor and health check, diagnostics | composition root; infrastructure layer for `AddPheme` with the module's handlers |
| `Pheme.FluentValidation` | validation behavior and validator registration | composition root |
| `Pheme.EntityFrameworkCore` | domain events published when a `DbContext` saves, outbox table, writer and store | infrastructure layer |
| `Pheme.Testing` | outbox helpers for integration tests: deliver at chosen points, read and redeliver events; layer rules for architecture tests | test projects |

The packages are on [nuget.org](https://www.nuget.org/packages/Pheme); installing needs no configuration. Trying
unreleased code from this repository is described in
[Consuming Pheme](https://github.com/dawid1103/Pheme/blob/master/docs/consuming.md).

```sh
dotnet add package Pheme
dotnet add package Pheme.FluentValidation        # optional
dotnet add package Pheme.EntityFrameworkCore     # optional
dotnet add package Pheme.Testing                 # optional, in test projects
```

## Quick start

A command and its handler, from the
[example application](https://github.com/dawid1103/Pheme/blob/master/examples/README.md) (shortened):

```csharp
public sealed record PlaceOrderCommand(string Product, int Quantity, bool Express = false) : ICommand<Guid>;

internal sealed class PlaceOrderCommandHandler(IOrderingDbContext db) : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Guid> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        Order order = Order.Place(command.Product, command.Quantity, command.Express);
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        return order.Id;
    }
}
```

Pheme registered once, with the assemblies holding the handlers; a message without a handler, or with two, fails the
start of the application:

```csharp
builder.Services.AddPheme(options => options.AddHandlersFromAssemblyContaining<IOrderingDbContext>());
```

Sent through `ISender`:

```csharp
public sealed class OrdersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Place(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        Guid id = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }
}
```

The rest - validation, request logging, the application's behaviors, domain events published on save - is in
[Getting started](https://github.com/dawid1103/Pheme/blob/master/docs/guide/02-getting-started.md).

## Documentation

[The documentation](https://github.com/dawid1103/Pheme/blob/master/docs/README.md) is a book to read in order: a
guide from installation to the outbox and tracing, recipes of pipeline behaviors (transactions,
authorization, caching, idempotency, audit), and the design decisions behind it all.

## Example

[`examples/ModularMonolith`](https://github.com/dawid1103/Pheme/tree/master/examples/ModularMonolith) is a small shop
built the way real applications use Pheme: an ordering module with one assembly per layer and a shipping module with
layers as folders. Every feature has a use case there with an end-to-end test over HTTP on PostgreSQL;
[`examples/README.md`](https://github.com/dawid1103/Pheme/blob/master/examples/README.md) maps each use case to its
code and tests.

## Versioning

Semantic versioning from 2.1.0: fixes in 2.1.x, features in 2.x, breaking changes only in a new major version.

## License

[MIT](https://github.com/dawid1103/Pheme/blob/master/LICENSE).
