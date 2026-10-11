# Pheme documentation

Pheme is lightweight in-process messaging for .NET 10: commands and queries with one handler each, an ordered pipeline
of behaviors, domain events published when Entity Framework Core saves, and integration events delivered through an
outbox. This documentation is written to be read in order, chapter by chapter; each chapter ends with a link to the
next. Every feature has a use case in the [example application](https://github.com/dawid1103/Pheme/blob/master/examples/README.md),
a small shop with an end-to-end test for each, and the snippets link to its files.

[How Pheme works](how-it-works.html) shows all of it on one page, with diagrams - an HTML page, to open in a browser
or on the documentation site.

## Guide

1. [Installation](guide/01-installation.md) - the packages and which layer references which package.
2. [Getting started](guide/02-getting-started.md) - one feature end to end: a command, a query, a validator and
   domain events, in nine steps.
3. [Commands and queries](guide/03-commands-and-queries.md) - one handler per message, the start-up check,
   scanning, modules and scopes.
4. [Pipeline behaviors](guide/04-pipeline-behaviors.md) - the application's own cross-cutting code around every
   message, and its order.
5. [Validation](guide/05-validation.md) - FluentValidation as a ready-made behavior.
6. [Request logging](guide/06-request-logging.md) - one log per message with its outcome and time.
7. [Domain events](guide/07-domain-events.md) - handlers in the same transaction, published when a `DbContext` saves.
8. [Integration events and the outbox](guide/08-integration-events.md) - events written with the change and
   delivered after it is saved, at least once.
9. [Operating the outbox](guide/09-operating-the-outbox.md) - parked messages, the health check, changing an event.
10. [Layers](guide/10-layers.md) - what each layer may use of Pheme, kept by architecture tests.
11. [Testing](guide/11-testing.md) - unit tests of handlers, integration tests with the outbox under control.
12. [Tracing and metrics](guide/12-tracing-and-metrics.md) - spans and measurements for OpenTelemetry.
13. [Configuration](guide/13-configuration.md) - the options, and setting them from `appsettings.json`.
14. [Limitations](guide/14-limitations.md)

## Recipes

Pipeline behaviors an application writes itself, each with a use case in the example:

- [Transactions](recipes/transactions.md) - a transaction of its own for the commands that ask for it.
- [Authorization](recipes/authorization.md) - a role named in the message's type, checked before validation.
- [Caching queries](recipes/caching-queries.md) - answers from `HybridCache` for a while.
- [Idempotent commands](recipes/idempotent-commands.md) - a retried request answers the first response.
- [Audit trail](recipes/audit-trail.md) - who did what, saved with the change.

## More guides

- [Consuming Pheme](consuming.md) - installing from nuget.org, and local packages of unreleased code.

## Behind the scenes

- [Design decisions](design.md) - why Pheme behaves the way it does, and the alternatives that were rejected.
- [How Pheme works inside](internals.md) - the code, class by class.
- [Developing Pheme](development.md) - the conventions of this repository, its tests, CI and releases.
