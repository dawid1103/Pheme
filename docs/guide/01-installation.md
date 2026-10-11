# Installation

Pheme is five packages, one per layer of an application that uses it, so each layer references only what it needs.

## Packages

| Package | Contents | Used by |
|---|---|---|
| `Pheme.Abstractions` | message, handler and behavior contracts; no dependencies | application layer |
| `Pheme` | `AddPheme`, sender, pipeline, request logging, domain event publisher, outbox processor and health check, diagnostics | composition root; infrastructure layer for `AddPheme` with the module's handlers |
| `Pheme.FluentValidation` | validation behavior and validator registration | composition root |
| `Pheme.EntityFrameworkCore` | domain events published when a `DbContext` saves, outbox table, writer and store | infrastructure layer |
| `Pheme.Testing` | outbox helpers for integration tests: deliver at chosen points, read and redeliver events; layer rules for architecture tests | test projects |

The packages are on [nuget.org](https://www.nuget.org/packages/Pheme); installing needs no configuration. Trying
unreleased code is described in [Consuming Pheme](../consuming.md).

Every public type is in the namespace `Pheme`; the registration methods are in the namespaces of what they extend
(`Microsoft.Extensions.DependencyInjection`, `Microsoft.EntityFrameworkCore`).

Integration events with an outbox are in the same packages: contracts in `Pheme.Abstractions` (`IIntegrationEvent`,
`IIntegrationEventHandler<TEvent>`, `IOutboxWriter<TModule>`), delivery in `Pheme`, the outbox table, writer and store
in `Pheme.EntityFrameworkCore`, helpers for integration tests in `Pheme.Testing` ([Integration events and the
outbox](08-integration-events.md)). `IOutboxStore` and `IOutboxMessageFactory` in `Pheme.Abstractions` are for
implementing a store of another kind of storage, not for application code.

## What goes where

| Piece | Layer | Pheme package |
|---|---|---|
| Entities, domain events, the entity contract (`IHasDomainEvents` in the example) | Domain | none |
| Integration events (`IIntegrationEvent`), shared by the modules | contracts (shared kernel) | `Pheme.Abstractions` |
| Commands, queries, their handlers and validators; domain and integration event handlers; the module marker and `IOutboxWriter<TModule>`; behaviors working with the module's `DbContext` | Application | `Pheme.Abstractions` |
| `DbContext` with `AddPhemeOutbox()`, `AddPhemeDbContext<TContext>` with `UseOutbox<TModule>()`; `AddPheme` with the module's assembly | Infrastructure | `Pheme.EntityFrameworkCore`, `Pheme` |
| `AddPheme` with `AddOutbox()`, the contracts' assembly, behaviors, controllers sending through `ISender`, `AddPhemeOutboxCheck()`, an administration endpoint with `IOutboxMaintenance` | API (composition root) | `Pheme`, `Pheme.FluentValidation` |
| Integration tests, architecture tests | tests | `Pheme.Testing` |

```sh
dotnet add src/Shop.Application package Pheme.Abstractions
dotnet add src/Shop.Infrastructure package Pheme.EntityFrameworkCore
dotnet add src/Shop.Infrastructure package Pheme
dotnet add src/Shop.Api package Pheme
dotnet add src/Shop.Api package Pheme.FluentValidation      # optional
dotnet add tests/Shop.Tests package Pheme.Testing          # optional
```

`Pheme.FluentValidation` is needed only for validation with FluentValidation, `Pheme.EntityFrameworkCore` only for
domain events published on save or for the outbox. An application of one project references `Pheme` and the optional
packages it uses. With layers as folders of one project, that project references `Pheme.Abstractions`,
`Pheme.EntityFrameworkCore` and `Pheme`, and the API the rest. See [Layers](10-layers.md) for the rules and the
architecture tests that keep them.

---

[Contents](../README.md) · Next: [Getting started](02-getting-started.md)
