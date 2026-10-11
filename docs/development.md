# Developing Pheme

Conventions of this repository and the reasons for them. Library design is in [`design.md`](design.md), how the
code works in [`internals.md`](internals.md), trying unreleased packages in an application in
[`consuming.md`](consuming.md).

## Layout

```
Pheme.slnx
src/Pheme.Abstractions/          contracts, no dependencies
src/Pheme/                       AddPheme, PhemeOptions; folders Sending, Pipeline, DomainEvents, Outbox,
                                 Registration, Logging, Diagnostics, Internal
src/Pheme.FluentValidation/      validation behavior, validator scanning
src/Pheme.EntityFrameworkCore/   domain event dispatch interceptor, outbox table, writer and store
src/Pheme.Testing/               outbox helpers for integration tests, layer rules for architecture tests
tests/Pheme.UnitTests/           Pheme and Pheme.FluentValidation
tests/Pheme.TestFixtures/        messages and internal handlers in a separate assembly
tests/Pheme.EntityFrameworkCore.UnitTests/         interceptor on SQLite in memory, registration, outbox model
tests/Pheme.EntityFrameworkCore.IntegrationTests/  outbox writer and store, Pheme.Testing, on PostgreSQL
tests/Pheme.EntityFrameworkCore.TestFixtures/      contexts, modules and integration events of both
tests/Pheme.Testing.UnitTests/   layer rules of Pheme.Testing, on fixture namespaces and assemblies
examples/ModularMonolith/        Shop.Api, Shop.Common, Ordering.{Domain,Application,Infrastructure}, Shipping
tests/Pheme.Examples.UnitTests/         layer tests of the example (reflection)
tests/Pheme.Examples.IntegrationTests/  end-to-end over HTTP, PostgreSQL in Testcontainers
docs/README.md                   contents of the documentation; guide/ chapters, recipes/ behavior recipes
mkdocs.yml                       the documentation as a site
```

- **Namespaces:** every public type is in `Pheme` (one `using`), DI extensions in
  `Microsoft.Extensions.DependencyInjection`, EF Core extensions in `Microsoft.EntityFrameworkCore`. Internal types
  use the namespace of their folder (`Pheme.Sending`, ...); IDE0130 is off for `src/**`.
- **Files** of generic variants carry a suffix instead of brackets, when a type of the same name and another arity has
  the plain name: `ICommandOfTResponse.cs` next to `ICommand.cs`. A generic type alone in its name keeps the plain
  name (`MessageHandlerWrapper.cs`). An internal base that only gives the cached closed generic types one untyped
  entry point is an interface (`IMessageHandlerWrapper<TResponse>`, `IDomainEventHandlerWrapper`,
  `IIntegrationEventHandlerWrapper`), not a non-generic class of the same name; shared code lives in the generic class.

## Build

- `TreatWarningsAsErrors`, `Nullable`, `Deterministic` everywhere; `GenerateDocumentationFile` and
  `AnalysisLevel=latest-recommended` for `src` only (CA1707 conflicts with underscores in test names), so CA rules are
  build errors in the library. A trial on the tests (2026-10-11) found little else: names (xUnit collections end with
  `Collection`) and a test logger factory that is disposable by its interface only.
- NuGet audit warnings `NU1901`-`NU1904` are not errors: a newly published advisory must not break the build without a
  code change.
- Code style is enforced in the build (`EnforceCodeStyleInBuild`): block-scoped namespaces (IDE0160 as a warning),
  4 spaces, LF line endings (`.gitattributes`).
- Rule suppressions are local (`SuppressMessage` with a justification) and only for names that are deliberate API:
  CA1711 (`MessageHandlerDelegate`, `IDomainEventHandler`, `IIntegrationEventHandler`), CA1716 (parameter `next`),
  RS0026 (optional `CancellationToken` on `ISender` and `IDomainEventPublisher`). CA2007 (`ConfigureAwait`) is off - see
  `design.md`.
- `global.json` pins SDK `10.0.100` with `rollForward: latestFeature` and opts into Microsoft.Testing.Platform.
- `nuget.config` clears all sources, so the build does not depend on the machine's configuration (a private feed on
  a developer machine once made restore hang). Dependencies, and the baseline of package validation, come from
  nuget.org only, as do the vulnerability data (`auditSources`); a restore needs no account or token.

## Licensing

- The dependencies, transitive ones included, are MIT, Apache-2.0 or PostgreSQL licensed, so no third-party notices
  file.
- Only general code, written in this repository; no files copied from other projects.

## Packages

- **Central package management:** versions only in `Directory.Packages.props`, with transitive pinning. Libraries
  reference the lowest version of the .NET 10 band; examples use the newest. Pinning is off in `examples/*`,
  the test projects of the example and of `Pheme.EntityFrameworkCore` - it would pin the newer EF Core's
  `Microsoft.Extensions.*` down to the libraries' 10.0.0 (NU1109). The setting in `Directory.Packages.props` is
  conditional so a project can turn it off. A package referenced by a library and directly by the example
  (`Microsoft.EntityFrameworkCore.Relational`) has the library's version there and `VersionOverride` in the example.
- **Public API snapshot:** `Microsoft.CodeAnalysis.PublicApiAnalyzers` with `PublicAPI.Shipped.txt` /
  `PublicAPI.Unshipped.txt` per package. A public API change breaks the build until it is listed in `Unshipped`, so it
  is visible in review. A removed API stays in `Shipped` and is listed in `Unshipped` with the prefix `*REMOVED*`. At
  each release the content of `Unshipped` moves to `Shipped`, and both lines of a removed API go.
- **Package validation** (`EnablePackageValidation`) checks the package itself and, once a version is on nuget.org,
  compares it with the baseline `PackageValidationBaselineVersion` (the last release, `src/Directory.Build.props`; none
  before 2.1.0, the first version on nuget.org), so a breaking change fails `dotnet pack` (`CP0002`, ...). Every
  restore downloads the baseline, so only CI (`GITHUB_ACTIONS`) sets it; locally
  `-p:PackageValidationBaselineVersion=X.Y.Z`. A package without a release clears the property in its project file
  until its first release. An intended breaking change
  (the next major version) goes into the suppression file `CompatibilitySuppressions.xml` of the project:
  `-p:ApiCompatGenerateSuppressionFile=true`. Without the baseline its suppressions suppress nothing, so outside CI they
  are permitted to (`ApiCompatPermitUnnecessarySuppressions`) - otherwise a local `dotnet pack` fails; CI still fails on
  a stale one.
- **Metadata** in `src/Directory.Build.props` (`Description` per project): MIT, repository README as the package
  readme, icon (`src/icon.png`, 128x128), release notes (a link to `CHANGELOG.md`), SourceLink, PDB embedded in the assembly (one file per package, no
  symbol server needed; `snupkg` would make the package smaller but gives symbols only from nuget.org's symbol
  server), `EmbedUntrackedSources`, `VersionPrefix` overridden by the publish workflow. Links in the README are
  absolute, because it is also the package
  description.
- Tests and examples are not packable. Packing goes project by project (`src/*/*.csproj`): packing the solution warns
  for the web project of the example.

## Tests

- xUnit v3 on Microsoft.Testing.Platform (the .NET 10 SDK no longer runs xUnit v3 through VSTest), without
  `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`.
- **Unit and integration tests are separate projects, and the name says which:** `<Package>.UnitTests` need nothing
  outside the process, `<Package>.IntegrationTests` need Docker (PostgreSQL in Testcontainers, the example over HTTP).
  SQLite in memory counts as unit: it runs in the process, without Docker or network. Test types used by several projects
  live in a library ending with `TestFixtures`. `tests/Directory.Build.props` gives every test of a project the trait `Category`
  (`Unit` or `Integration`) from that suffix, and fails the build of a project under `tests/` with another name, so a
  test cannot end up in neither run.
  - `dotnet test` runs both and needs Docker; `dotnet test --filter-trait "Category=Unit" --ignore-exit-code 8` the
    unit tests without Docker, `--filter-trait "Category=Integration" --ignore-exit-code 8` the integration tests
    (exit code 8: a project of the other kind ran none). One project: `dotnet test --project tests/<Project>`.
  - A new test goes into the project of the package it tests and of its kind; a test project of a new kind of test
    needs a suffix in `tests/Directory.Build.props` and a step in `ci.yml`.
  - Projects rather than a trait per class: the kind shows in the solution and on the path, and an
    integration test cannot forget its trait - the unit project does not reference Testcontainers.
- Assertions with xUnit's `Assert` only. FluentAssertions 8 has a commercial licence.
- `Pheme.TestFixtures` holds public messages and `internal` handlers and validators without `InternalsVisibleTo`,
  to prove that scanning finds types the calling code cannot see. Tests needing such types get them by reflection.
- Event and handler types of the tests follow the example's naming ("Names show the kind" below):
  `ShippedDomainEvent`, `OrderPlacedIntegrationEvent`, `RecordingIntegrationEventHandler<TEvent>`. The same name in
  two test projects never stands for two kinds of event.
- Diagnostics tests run in a collection with `DisableParallelization`: `ActivityListener` and `MeterListener` are
  global, parallel tests would see each other's spans.
- EF Core tests of the interceptor use SQLite in memory; the outbox tests run on PostgreSQL in Testcontainers (Docker
  required), one container per test run and one database per test (`PostgresDatabase`). The outbox tests are abstract
  (`OutboxWriterTests`, `OutboxStoreTests`, `OutboxTestingTests` over `ITestDatabase`), run by a derived class per
  supported database. Example tests start PostgreSQL in Testcontainers as well; its layer tests are pure reflection, in
  `Pheme.Examples.UnitTests`.
- `Pheme.Testing.UnitTests` tests the layer rules on namespaces of fixture types (`LayerFixtures.cs`: `Valid.*` keep
  their rules, `Broken.*` break them one way per type) and on the assemblies of Pheme and of the tests, whose
  references are known. Its assembly is named `LayerRuleTests`: the rules take every `Pheme.*` assembly for one of
  Pheme, the fixtures' own too.
- **Connection strings of test containers carry `GSS Encryption Mode=Disable`.** Npgsql negotiates GSS encryption
  whenever the machine holds a Kerberos ticket (`klist`); with the KDC unreachable, for example outside a VPN, each
  attempt hangs ~40 s, and without the setting the first connection of a test run times out after ~80 s.
- **Times in tests on PostgreSQL are in whole microseconds.** `timestamptz` keeps microseconds, `DateTimeOffset`
  100 ns ticks (`UtcNow` on Linux uses them): a time read back can be earlier than the one written, and a test at a
  boundary (the outbox health check after exactly 5 minutes) fails on Linux only. A `FakeTimeProvider` starts on a whole
  microsecond.
- A test of an order or a rule is checked by mutation: break the code, the test must fail.

### Adding a database

The outbox supports the databases its tests pass on (`docs/design.md`, "Supported databases"). To add one:

1. The EF Core provider and a Testcontainers module in `Directory.Packages.props` (group "Tests"), referenced by
   `Pheme.EntityFrameworkCore.IntegrationTests`.
2. A fixture next to `PostgresDatabase.cs`: an `ITestDatabase` with a container, its collection, and the three derived
   classes `<Database>OutboxWriterTests`, `<Database>OutboxStoreTests` and `<Database>OutboxTestingTests`.
3. The provider name (`context.Database.ProviderName`, the provider's assembly name) and the database name in
   `OutboxDatabases`.
4. `dotnet test`; a failure is a difference of the database, fixed inside `Pheme.EntityFrameworkCore` (mapping in
   `AddPhemeOutbox`, a query of `EfOutboxStore`), never in the shared tests.
5. The database in the XML docs of `AddPhemeOutbox` and `UseOutbox`, in the chapters "Integration events and the
   outbox" and "Limitations" of the guide, and in the unsupported-database test of `OutboxRegistrationTests`.

## CI and publishing

- Branches: `develop` for ongoing work (CI only), `master` - the default branch - for releases.
- `ci.yml`: on push to `develop` and `master` and on pull requests; `concurrency` cancels older runs of a pull request
  only, so every commit on `develop` and `master` has its own result. A NuGet cache keyed by `global.json`,
  `nuget.config` and `Directory.Packages.props` (no lock files - CPM already pins versions),
  `dotnet format --verify-no-changes`, Release
  build, unit tests, then integration tests (including the examples - `ubuntu-latest` has Docker) as a step of their
  own, so a failure says which kind failed (`--filter-trait "Category=..."`, "Tests"), pack as a run artifact, never
  pushed.
- `docs.yml` builds the documentation site and deploys it by hand ("Documentation").
- `.github/dependabot.yml`: weekly updates of NuGet packages and actions. The packages the libraries reference are not
  updated this way (they stay at the lowest version of the band, "Packages"); security updates still arrive.
- `ContinuousIntegrationBuild` is set from `GITHUB_ACTIONS`, so it also applies to `dotnet pack`.
- Workflows have `contents: read` and `persist-credentials: false`; they write nothing to the repository.
- `publish.yml`: a tag `v*` on a commit of `master` (checked, otherwise the run fails) tests, packs with the version
  of the tag (assemblies get the same version), pushes the packages to nuget.org (`--skip-duplicate`) and creates a
  GitHub Release. A version with a suffix (`v2.2.0-preview.1`) is published too and marked as a pre-release.
- **Why nuget.org, and why this way.** A version on nuget.org can only be unlisted, never deleted, and the package
  links to the source through SourceLink, so the repository had to be public first and every release needs a second
  look: the job runs in the environment `nuget`, which waits for the maintainer's approval. The tag that starts it is
  protected the same way (only the maintainer creates `v*` tags). The push uses Trusted Publishing instead of an API
  key: `NuGet/login` exchanges the workflow's OIDC token for a key valid for about an hour, once nuget.org has a policy
  matching the repository, the file name `publish.yml` and the environment, so there is no long-lived secret to leak or
  renew. The username goes in the repository variable `NUGET_USER` (not a secret). The actions of this workflow are
  pinned to a commit, because it can publish. The PDB stays embedded in the assembly: one file per package, and
  SourceLink needs no symbol server.
- Semantic versioning, from 2.1.0, the first version on nuget.org.

## Documentation

- **A book in `docs/`**, read on GitHub or as a site: `docs/README.md` is the contents, `docs/guide/` the chapters in
  reading order (numbered file names, so GitHub lists them in that order), `docs/recipes/` the behavior recipes. The
  other files of `docs/` (`design.md`, `internals.md`, this file, `consuming.md`) stay where code comments point to
  them. Each chapter and recipe ends with links to the previous one, the contents and the next one, which the site does
  not repeat (no `navigation.footer`).
- **`docs/how-it-works.html`** shows the whole book on one page with SVG diagrams: one self-contained file (inline CSS
  and SVG, no script, no build, light and dark mode from the system), which MkDocs copies to the site as it is, linked
  in `nav`. Its links point to the site's pages (`guide/07-domain-events/`), since on GitHub an HTML file shows as
  source. A change of the book that the page summarizes changes the page too.
- **The repository README is the package description** (`PackageReadmeFile`), shown by the feed outside the
  repository: what Pheme is, the packages, a quick start, and absolute links into the book. Everything else is in the
  book only, so a fact has one place.
- **Links** between files of `docs/` are relative, so they work on GitHub and on the site; links to anything outside
  `docs/` (the example, `src/`, tests) are absolute to `master`, because the site resolves only the files of `docs/`.
  Every feature described has a use case in the example, and the snippets link to its files.
- **The site:** `mkdocs.yml` with Material for MkDocs (MIT) - navigation, search, dark mode - with no plugins, theme
  overrides or code of our own. `docs.yml` builds it with `--strict` (a broken link or anchor fails) for pushes to
  `develop` and `master` and pull requests that change `docs/` or `mkdocs.yml`, in the image
  `squidfunk/mkdocs-material` of a pinned version, and deploys to GitHub Pages only when run by hand: Pages of a
  private repository needs a paid plan, and the site would be public anyway. Once the repository is public, the
  workflow gets a deploy on a push to `master`. Locally, from the repository root:
  `docker run --rm -it -p 8000:8000 -v "$PWD:/docs" squidfunk/mkdocs-material:9.7.7`, then `http://localhost:8000`.
- **Material for MkDocs is in maintenance mode** (since November 2025; its authors continue in Zensical, which reads
  `mkdocs.yml`), and MkDocs 2.0 will not run it - hence the pinned image, which carries MkDocs 1.x. Without plugins and
  overrides, moving to Zensical changes the build command, not the files. Rejected: DocFX (a .NET build for what are
  Markdown pages, API reference the XML docs already give in the IDE) and Markdown on GitHub only (no search).

## Releases

- **A dry run before a major version.** The applications using Pheme move to it on local packages
  ([`consuming.md`](consuming.md), "Local packages"), in a git worktree so other work in their repositories is not
  touched. The bar: it builds, the application's tests pass without changing their assertions, no workaround in the
  application. Every gap in the API is a Pheme task before the release; friction that is not a gap goes into the
  documentation.
- **Steps:** the content of each `PublicAPI.Unshipped.txt` moves to `PublicAPI.Shipped.txt`, `VersionPrefix` in
  `src/Directory.Build.props` and the date and entries in `CHANGELOG.md` are set, the commit goes to `master` and is
  pushed, then the tag `vX.Y.Z` on it. `publish.yml` checks the tag against `origin/master`, so the commit has to be
  pushed before the tag. The run waits for the approval of the environment `nuget`, then publishes and creates the
  GitHub Release. Check the result in a clean directory with only nuget.org as the source: `dotnet add package Pheme`.
- **A published version cannot be taken back:** it can only be unlisted. A mistake is fixed by the next patch version,
  never by publishing the same number again.
- **After the release:** `PackageValidationBaselineVersion` in `src/Directory.Build.props` is set to it, for CI only
  (a property group conditioned on `GITHUB_ACTIONS`), so package validation compares the next version with it, and the
  suppression files of its breaking changes are deleted.
- Breaking changes of a major version get an "Upgrading from" chapter in the guide and an entry in `CHANGELOG.md`.

## Example

`examples/ModularMonolith` defines "works": every feature has a use case with an end-to-end test over HTTP on
PostgreSQL. The domain is a neutral shop with two module styles: Ordering with one assembly per layer, Shipping with
layers as folders of one assembly.

- **`Shop.Common`** is the shared kernel: application exceptions (`ValidationException` with a field → messages
  dictionary, without FluentValidation; the mapping is the factory in `Program.cs`), contracts between modules (the
  integration events each module writes to its outbox), the current user and roles (`ICurrentUser`, `IRequireRole`),
  cacheable queries (`ICachedQuery`), schema creation. The contracts hold no commands: a module sending
  a command to another through `ISender` saves in two transactions, so the example once cancelled the shipment that
  way and lived with the gap; the cancellation now goes through the outbox like the other flows between the modules.
  With no messages that need a handler, `AddPheme` does not add the assembly.
- **Modules work together through the outbox only, in both directions** (Ordering → Shipping: placed, cancelled;
  Shipping → Ordering: dispatched), each module with its own outbox. A module decides from its own data:
  Ordering refuses to cancel a shipped order (422) once `ShipmentDispatchedIntegrationEvent` is delivered, not by asking
  Shipping. Until then both changes may be accepted; the one landing second settles it - a cancellation reaching a
  dispatched shipment asks for the parcel back (`ShipmentStatus.ReturnRequested`, a warehouse notice), and the dispatch
  reaching a cancelled order leaves it cancelled. The confirmation handler of `OrderPlacedIntegrationEvent` reads the
  order of its own module and skips a cancelled one, so a customer is not told of an order cancelled before the
  delivery.
- **Schema:** `EnsureCreated` does nothing once the database has any table, so with a schema per module in one
  database only the first module would get its tables. `EnsureSchemaCreatedAsync` creates the database if missing and
  the tables if the context's default schema is missing; `Program.cs` calls it before `RunAsync`.
- **No connection string in `appsettings*.json`**: a broken override in tests would write to a developer's local
  database. Startup fails with a hint (`ConnectionStrings__Shop`); tests set it with `UseSetting`.
- **`AddPheme` only in `Shop.Api`**; modules register their infrastructure (`AddOrdering`, `AddShipping`), the
  domain event dispatch of their entities and their outboxes (`UseOutbox<OrderingModule>`, `UseOutbox<ShippingModule>`). Handlers whose
  order matters are registered by hand in `Ordering.Application` before `AddPheme`. The feature folder is
  `Inventory`, not `Stock`: with `Stock` the scan order would equal the manual order by accident and the order test
  would prove nothing (`The_scan_alone_would_run_the_handlers_the_other_way_round` guards that).
- **Names show the kind** of a message and of a handler (`OrderPlacedDomainEvent`, `AllocateStockDomainEventHandler`,
  `PrepareShipmentIntegrationEventHandler`), as recommended in the guide ("Getting started", "Naming"): the example's
  events once had no suffix and its handlers were named after an action only, so a handler did not tell which kind of
  event it handled. A handler is named after what it does; the handler writing the integration event is
  `WriteOrderPlacedToOutboxDomainEventHandler`, not "Publish...", which reads like publishing a domain event. Domain
  events live in an `Events` folder next to their aggregate.
- **Layer tests** use `PhemeLayerRule` of `Pheme.Testing`, as an application would: `AssemblyLayerTests` (layer =
  project) and `NamespaceLayerTests` (layer = folder of the shipping assembly), a theory over the layers and a
  negative test each proving the rule can fail there.
- **A failed start is a unit test.** `ModuleRegistrationTests` registers the shop as `Program` does without the
  shipping module, and checks that the start reports the integration events left without a handler. Not end to end
  over HTTP: an application that does not start serves no request, and `Program` gets no switch to leave a module out
  only for a test.
- **Outbox in tests:** the shared application runs without the outbox processor (`UsePhemeOutboxForTests`) and the tests
  deliver with `DrainOutbox`. They share one outbox, so a drain delivers the messages of other tests too: the assertions
  look at one order, and the handlers are idempotent, so delivering another test's message again changes nothing. The services outside the application the handlers call (customer notifier, carrier) are fakes in
  `ShopFixture`; `FakeCarrier` fails on demand (a number of times, or until repaired). The test of the processor in the
  background starts a second instance of the application on the same database. Tests that count attempts or read the
  health of the outbox (`OutboxMaintenanceTests`, the cancellation crossing a failing placement in `OutboxTests`) and
  the tests of concurrency start an instance on a new database of their own
  (`CreateFactoryWithOwnDatabase`): a drain of the shared outbox runs more rounds while it delivers the messages of
  other tests, each a further attempt at a failed message, and a message parked by one test would make the outbox health
  check of the others unhealthy. `UsePhemeOutboxForTests` sets `RetryDelay` to zero, so each drain is one further
  attempt at a failed message. The test of the degraded outbox check replaces `TimeProvider` with a `FakeTimeProvider`,
  which dates the events and the check alike.
- **A cancellation delivered before the placement:** the messages of one outbox are tried in the order they occurred,
  so the cancellation overtakes the placement only while the placement's delivery fails. Shipping then saves the
  shipment as cancelled at once (`Shipment.CancelBeforePreparation`), without `ShipmentPreparationCancelledDomainEvent` -
  the warehouse prepared nothing - and the retried placement prepares nothing. The unique index on the order decides a
  race between the two deliveries: the losing one is retried and sees the other's shipment.
- **Concurrency:** the aggregates changed by concurrent requests or deliveries (`Order`, `ProductStock`, `Shipment`)
  have a row version, a shadow `uint` property with `IsRowVersion()`, which Npgsql maps to PostgreSQL's `xmin` system
  column, so the tables need no column of their own. A shadow property keeps it out of the domain. `ApiExceptionFilter`
  answers 409 for `DbUpdateConcurrencyException` and for a unique index violation (`23505`), which in the example only a
  concurrent save causes (two history entries numbered alike, two shipments of one order); `IsExpected` of the request
  logging treats both as expected. A delivery of the outbox that loses a race fails like any other and is retried, and
  the retry sees the change. The tests send the other request at a set point with a `SaveChangesInterceptor` registered
  after Pheme's (`BeforeNextSaveInterceptor`), right before the save writes and after its domain events are published;
  two requests started at once would race by chance.
- **The order history records what happened to the order only.** The stock reaching its reorder level is a fact about
  the stock: it requests a restock (`RestockRequest`) instead of writing "running low" into the history of the order
  that happened to allocate the last items, which also showed customers the stock level. The second round of dispatch
  is shown by that request.
- **Changing an integration event:** `OrderPlacedIntegrationEvent.Express` stands for a property added after the first
  version; the test rewrites a message to the JSON the previous version wrote and delivers it (guide, "Operating the
  outbox", "Changing an integration event").
- **Behavior recipes** (`docs/recipes`) live where their dependencies are: the authorization behavior in
  `Shop.Api`, as it needs the request's user, and the transaction behavior in `Ordering.Application`, as it opens the
  module's own context - so its marker, `ITransactionalCommand`, belongs to that module. A message names its role as a
  static abstract member of `IRequireRole` rather than an attribute: checked by the compiler, read without reflection.
  The example authenticates with request headers (`HeaderAuthenticationHandler`), a stand-in that keeps it free of an
  identity provider; handlers see the user through `ICurrentUser` of `Shop.Common`, so a real scheme changes nothing
  else. The tests send the headers in `ShopClient`. Receiving stock closes the restock requests with `ExecuteDelete`
  so that the transaction behavior has a direct write to protect; its test makes the save conflict and checks the
  delete is rolled back.
  The idempotency and audit behaviors live in `Ordering.Application` for the same reason as the transaction behavior;
  the caching behavior in `Shop.Api`, with `HybridCache` of the host.
- **OpenTelemetry** is configured in `Program.cs` as an application would, with the request and Npgsql spans around
  Pheme's, and exports only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, so tests and a plain `dotnet run` send nothing.
  `TelemetryTests` adds the in-memory exporter and continues a trace sent in `traceparent`: the request's own span may
  end after the client has the response, so the test checks the trace id rather than waiting for that span.
- `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes` in `AddControllers`, so a missing field is reported
  by the validator, not by MVC.
- Not production patterns: schema without migrations.
