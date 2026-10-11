# Design decisions

Why Pheme behaves the way it does. Usage is in the [guide](README.md), how the code does it in
[`internals.md`](internals.md); this file records the reasons and the alternatives that were rejected, so they are
not reopened by accident. Each decision favours convenience and safety of use.

## Scope

- **One way to do each thing.** No `Send(object)`, streams, one interface for sending and publishing, pre- and post-processors,
  exception handlers or parallel publishing. Less to learn, less to get wrong; anything added later in a minor version
  must be needed by an application.
- **Ready-made parts every application writes anyway**: request logging, validation, domain event dispatch on save,
  diagnostics. The application configures them instead of implementing them.
- **Nothing imposed on the domain.** Domain events and entities carrying them are application types; Pheme receives
  them through a generic type parameter or a delegate. There is no `IDomainEvent` marker.
- **Only general code.** A need specific to one application becomes an extension point (option, delegate, behavior)
  implemented by that application.

## Messages

- **`Task`, not `ValueTask`.** `ValueTask` must not be awaited twice or stored and does not fit `Func<Task>` in test
  assertions; the allocation it saves is not measurable next to handlers that go to a database.
- **Commands and queries instead of `IRequest`.** Intent is visible in the type and a behavior can be limited to
  commands. A command without a result returns `Task`; `Unit` is seen only by behavior authors. A command may return a
  light result (`ICommand<T>`: an id, an outcome). The split also gives
  `pheme.message.kind` and `MessageKind` in metrics and logs without configuration.
- **`ICommand<T>` does not inherit `ICommand`, and there is no common command marker.** A behavior for all commands
  is two classes (`IPipelineBehavior<TCommand, Unit>` and `IPipelineBehavior<TCommand, TResponse>`) or uses
  `IMessage`. Rejected: a marker such as `IBaseCommand` (can be added in a minor version without breaking anything if a
  need appears) and `ICommand : ICommand<Unit>` (puts `Unit` in front of every user).
- **No `Async` suffix**, as in `Send`, `Publish` and `Handle`: one convention for the whole library,
  the outbox and `Pheme.Testing` included (`RunOnce`, `DrainOutbox`, the methods of `IOutboxStore`).
- **`Unit` has no `Unit.Value`**; `default` is enough.
- **`ICommand<TResponse>` and `IQuery<TResponse>` are invariant.** With `out`, `ICommand<object> c = new X()`
  compiled and failed at run time with "no handler"; now it does not compile.

## Sending

- **`ISender` and `IDomainEventPublisher` are scoped**, not transient. Inside a request nothing
  differs: the sender is stateless and uses the provider of the scope it was resolved from. The difference is the
  mistake of injecting it into a singleton: transient silently resolves handlers and `DbContext` from the root
  provider, scoped fails at startup (`ValidateScopes` / `ValidateOnBuild`, on by default in Development). Code outside
  a request (`BackgroundService`) creates a scope.
- **No reflection per call, no source generators.** A wrapper per message type is created once
  (`MakeGenericType`) and kept in a static cache; for messages with a response the cache is a static generic class
  per `TResponse`, so no casts are needed. Source generators were rejected: they can break the build and need
  access to handlers, which are often `internal` in other assemblies.
- **Exceptions reach the caller unchanged** - no `TargetInvocationException` or `AggregateException`. Exceptions of
  handlers and behaviors, including ones thrown synchronously and DI resolution failures, end up in the returned
  `Task`. Argument checks (`null`) throw synchronously from `Send` / `Publish`, before anything runs.
- **Handlers are transient**, so a handler may depend on a scoped `DbContext` without becoming a captive
  dependency. The allocation is negligible.
- **No check against an `ISender` resolved from the root provider.** It would depend on how the Microsoft container
  exposes its root and would break legitimate use in consoles and tests, while `ValidateScopes` in Development
  (`WebApplicationFactory` included) already catches the sender injected into a singleton.
- **No `ConfigureAwait(false)`** (CA2007 off). After an `await` Pheme calls application code (behaviors, handlers);
  `ConfigureAwait(false)` would change the context that code runs on. ASP.NET Core has no `SynchronizationContext`,
  so there is nothing to gain.

## Registration

- **`AddPheme` may be called many times** (each module registers itself). The first call registers Pheme's services,
  later ones add assemblies. The shared state lives in the `IServiceCollection` as a singleton instance, found by its
  descriptor. Pheme registrations copied from another collection throw `InvalidOperationException` instead of silently
  writing to the old one.
- **`PhemeOptions` is per call, not `IOptions`.** It records the assemblies, behaviors and extensions of one call.
- **Handlers are always transient**, with no lifetime option. Rejected: a lifetime per `AddPheme` call - it costs a
  dictionary of lifetimes per assembly with its error, one instance per interface for a class handling several
  messages, and docs; `Singleton` is a trap (a handler with a `DbContext` from the root, caught by `ValidateScopes` in
  Development only), and resolving a transient handler costs a few bytes. A handler that needs
  another lifetime is registered by hand before `AddPheme`: the scan skips a class already registered under that
  interface (`TryAddEnumerable`).
- **Scanning** registers concrete closed classes, including `private` nested ones and classes derived from an
  abstract generic base; abstract and open generic types are skipped. Registration uses `TryAddEnumerable`: the same
  class under the same interface once, also when the application registered it by hand before. A class handling
  several messages gets one descriptor per interface.
- **Scan order is the full type name (ordinal).** `GetTypes()` has no documented order; sorting makes the order of
  event handlers and validators deterministic.
- **A broken assembly is a configuration error.** `ReflectionTypeLoadException` is not swallowed; it becomes an
  `InvalidOperationException` naming the assembly and the loader errors, with the original as inner exception.
- **Warning against `AppDomain.CurrentDomain.GetAssemblies()`**: it returns only the assemblies loaded so far.
- **`AddHandlersFromAssemblies` / `AddHandlersFromAssemblyContaining<T>`.** `Add`, like the rest of `PhemeOptions`
  (`AddBehavior`, `AddRequestLogging`, `AddAssemblyScanner`, `AddFluentValidation`). `Handlers`, not `Scan`: the name says what ends up in DI, not how.
  Extensions search the same assemblies for their own types (validators), documented on the method rather than
  named in it (`AndValidators`), because validation is optional. Rejected: `RegisterFromAssemblies` (reads like
  "register Pheme"); a separate list of validator assemblies (a second list to keep in sync, a missing entry silently
  skips validation); automatic discovery - `AppDomain.GetAssemblies()` sees only loaded assemblies, the entry
  assembly's references lose unused ones, `DependencyContext` picks up test projects and libraries shared with
  another host. The assemblies are named explicitly; a source generator could add discovery in a minor version without a
  breaking change.
- **Contract assemblies are added with the same method.** The start-up check knows the messages and integration
  events of the added assemblies only, so the contracts shared by modules (messages, integration events, no
  handlers) are added like a module; documented on the method and in the guide ("Commands and queries"). The example
  adds its contracts in the composition root, so leaving a module out fails the start for the events only that
  module handled. Rejected: an `AddMessagesFrom...` method (a second list, and the same scan); messages found through
  the handlers' signatures (a message with no handler at all is exactly the one not found); the assemblies of the
  handled messages and events checked automatically (it catches the module left out without configuration, but an
  application handling only part of a contracts assembly shared with another host could not start, with no way out,
  and commands and events would follow different rules).
- **A module registers its own handlers**: `AddPheme` with the module's assembly in the module's infrastructure, and one
  `AddPheme` with the behaviors, the outbox and the contracts in the composition root. Calls share one registry, and a
  scanner added later (`AddFluentValidation`) covers the assemblies of earlier calls, so the validators of a module are
  found whichever call comes first. The composition root does not collect the modules' assemblies (a list to keep
  complete, with a test that none is missing), and the layer rule of the infrastructure allows `Pheme` (see "Layer
  rules"). Rejected: a lighter registration of an assembly in a package the infrastructure already has
  (`Pheme.EntityFrameworkCore`) - new API only to keep `Pheme` out of the layer, and a second way to do what
  `AddHandlersFromAssemblies` does.
- **No shorter `AddPheme`.** The minimal configuration is one line (`o.AddHandlersFromAssemblyContaining<T>()`) and
  the rest is optional; `AddPheme(params Assembly[])` would be a second way to do the same.
- **`AddHandlersFromAssemblyContaining(params Type[])`** next to the generic overload: a static class, the natural
  marker of a module's registration, cannot be a type argument (`CS0718`), and `typeof(X).Assembly` in every call
  reads worse.

## Startup validation

- **Configuration errors fail the start of the application, not the first `Send`**, all of them in one exception:
  a message without a handler (with the assemblies that were searched), more than one handler for a message, a
  message implementing more than one of `ICommand`, `ICommand<T>`, `IQuery<T>` (reported only as ambiguous), a type
  with only `IMessage`, behaviors registered as the interface (`services.AddTransient(typeof(IPipelineBehavior<,>), ...)`).
- **Mechanism:** `IValidateOptions` on an internal marker options type plus `ValidateOnStart()` from
  `Microsoft.Extensions.Options`, so the host runs it without a dependency on hosting. The public `PhemeOptions` is
  per call, so `IOptions<PhemeOptions>` would be empty and misleading. Without a host (console, tests) the sender
  reads the same cached options when it is resolved, so validation runs once and a broken registration throws on
  every resolution of `ISender`.
- **The exception is `OptionsValidationException`** (what the host throws at startup) with a multi-line message,
  one `- ` line per problem.
- **Validation reads the collection at startup**, so it sees handlers registered by hand before and after `AddPheme`,
  and `RemoveAll` / `Replace` in an application's integration tests. Missing handlers are checked for concrete messages
  of the scanned assemblies; duplicates for all non-keyed handler registrations, including messages from other
  assemblies. Keyed registrations are skipped - the sender does not resolve them.
- **Duplicates say what happened.** One class registered twice (usually by hand after `AddPheme` already scanned it)
  is reported as that class registered N times, with the fix (remove the registration by hand, or keep it before
  `AddPheme` for another lifetime), not as "2 handlers: 'X', 'X'", which reads like a bug of Pheme. Different
  classes get a hint at `Replace` / `RemoveAll`, the way to swap a handler for a fake in tests. A missing handler at
  `Send` names `AddHandlersFromAssemblies`, as the start-up check does.
- **The outbox** is reported when integration event handlers are registered without it (`AddOutbox`) - nothing
  would deliver their events -, when it has no `IOutboxStore` - the processor would silently do nothing -, when a
  concrete integration event of the scanned assemblies has no handler (non-keyed) - the outbox would park it -, and
  when a handled event type cannot be delivered: an interface or abstract type (delivery is by exact runtime type), a
  generic type, or a stored name that is empty or shared by two event types. The delivered event types are those of
  the registered handlers, scanned or by hand; an event of another assembly whose runtime type has no handler fails
  when it is written ("Outbox", "An event without a handler is rejected when written").
- **An integration event without a handler is checked only with the outbox.** Without `AddOutbox` no event can be
  written (`UseOutbox` requires it), so the event is harmless, and a host without the outbox may scan the contracts.
  Always on with the outbox, without an option: the writer and the handlers are in one application, so an event
  without a handler is a mistake - an old version kept until the table is drained still has its handler. Rejected:
  the check without the outbox (a host without it would fail on events it can never write, and its message would
  be followed by "handlers without the outbox" once a handler is added); an option per event (no case needs one).
- **Domain event handlers** are reported only when they can never run: a non-keyed handler of an interface or
  abstract event type (the publisher resolves handlers by exact runtime type, which is never abstract). The number
  of handlers is not validated - any number, zero included, is fine. The hint points to an open generic handler
  registered by hand, the way to handle every event of a kind.
- **Not validated:** behaviors (an invalid type is rejected by `AddBehavior`; a behavior that matches no message is
  not an error).

## Pipeline

- **One pipeline for all `AddPheme` calls**, in the order of the calls; the same behavior type again is skipped, so
  modules may add the same behavior. A behavior for one module only is a generic constraint (a module marker on its
  messages).
- **Order is the order of `AddBehavior`, outermost first**, independent of the DI container. A behavior is
  registered as a service of its own type and resolved by its closed type, so the order does not depend on whether
  the container skips open generics with unsatisfied constraints in `IEnumerable<>`. The application may register a
  behavior itself, before `AddPheme`, with a different lifetime.
- **Open generic behaviors are closed by inference from their `IPipelineBehavior<,>` interface**, not by parameter
  position, so `X<TCommand> : IPipelineBehavior<TCommand, Unit>` and `X<TQuery, TItem> : IPipelineBehavior<TQuery,
  TItem[]>` work. Every type parameter must appear in that interface, otherwise `AddBehavior` throws
  `ArgumentException`. Constraints are checked by hand when the list is built, so a behavior that does not fit a
  message is skipped without an exception.
- **The behavior list is computed once per message type and service collection.** Wrappers are static and shared by
  all providers, behaviors belong to a collection (tests, `WebApplicationFactory`), so the list is cached in a
  `ConditionalWeakTable` keyed by the collection's behavior list and versioned - a behavior added after the first
  `Send` and a new `BuildServiceProvider` is picked up.
- **The handler is resolved in the innermost step**, so a behavior that short-circuits never creates it. Behaviors are
  created up front: they seldom short-circuit, and creating them lazily would cost a closure each.
- **Cost** (.NET 10, Release, a handler completing synchronously): `Send` allocates 64 B, of which
  24 B are resolving and calling a transient handler without Pheme; each behavior adds up to ~200 B. Validation is
  dominated by FluentValidation itself (~2.9 KB for a new transient validator with one rule). Rejected for gains of
  this size: a pipeline without closures (a chain of objects, ~100 B per behavior, harder code).

## Extension packages

- **`PhemeOptions.Services` and `PhemeOptions.AddAssemblyScanner`** (marked `EditorBrowsable(Advanced)`) let
  `Pheme.FluentValidation` plug into `AddPheme`. No `InternalsVisibleTo` between packages: with different versions of
  `Pheme` and an extension, internal API would fail with `MissingMethodException`.
- **A scanner runs for the assemblies of every `AddPheme` call** - earlier, current and later, including calls that
  did not add it. The behavior is global, so scanning only the calling module would silently skip other modules'
  validators. The same scanner is applied to each assembly once.
- **Overloads instead of optional parameters** (`AddFluentValidation()` / `AddFluentValidation(factory)`,
  `AddRequestLogging()` / `AddRequestLogging(configure)`) keep binary compatibility when parameters are added.

## Validation (`Pheme.FluentValidation`)

- **Validators run one after another, not in parallel.** An async validator often queries the database through a
  scoped `DbContext`, which is not thread-safe.
- **Each validator gets its own `ValidationContext`**; a shared one accumulates failures of earlier validators and
  would duplicate them. Failures of all validators are collected and the handler is not called.
- **Any failure stops the message**, `Severity.Warning` included, as `ValidateAndThrow` does.
- **Validators stay transient.** Singleton validators (always, or with a parameterless constructor) would save ~2.2 KB
  and ~1 µs per `Send` with one rule - not measurable next to a handler that goes to a database - and risk a captive
  dependency (a validator using a scoped `DbContext`).
- **The behavior applies to every message**, also one without validators (~200 B): behaviors are closed without a
  provider, so the pipeline cannot know which messages have validators.
- **Validators by exact message type** (`IValidator<TMessage>`, no variance), like domain event dispatch.
- **Validators are always registered from the scanned assemblies** (all concrete closed `IValidator<T>`, also for
  non-message types injected into other validators), transient, with `TryAddEnumerable`. A validator registered twice
  runs once (deduplicated by type in the behavior). Own scanning instead of
  `FluentValidation.DependencyInjectionExtensions` - one dependency less.
- **One exception factory per service collection.** A call without a factory keeps the one set elsewhere, a second
  different factory throws immediately, so modules can call `AddFluentValidation()` and the composition root sets the
  factory in any order. A factory returning `null` throws `InvalidOperationException`.
- **The validation exception is not "expected" for request logging automatically** - the package cannot know the
  type the application's factory creates. [Request logging](guide/06-request-logging.md) shows how to add it to
  `IsExpected`.

## Request logging

- **Built in and always outermost**, regardless of the order of `AddBehavior` and `AddPheme` calls, added once.
- **Options through `IOptions`**: several `AddRequestLogging` calls configure one instance in call order and
  `services.Configure<RequestLoggingOptions>` works. `IsExpected` is last-wins; the XML docs show how to extend the
  previous predicate.
- **Requires `ILoggerFactory` from DI**, without a fallback. A `TryAdd` of `NullLoggerFactory` would beat the
  application's own `AddLogging` (also `TryAdd`), and silent missing logs are worse than an error at the first `Send`.
- **Messages** (`[LoggerMessage]`, event ids 1-3, category `Pheme.Requests`):
  `"{MessageKind} {MessageType} handled in {ElapsedMs:0.0} ms"`, and on failure
  `"... failed in {ElapsedMs:0.0} ms with {ExceptionType}"` - plus `": {ExceptionMessage}"` and no stack trace for an
  expected failure, the exception itself for an unexpected one. `MessageKind` is part of the text because otherwise it
  would not be a structured property without a custom state type. `MessageType` is the full name, because the bare
  name of `PlaceOrder.Command` would be ambiguous. Elapsed time is formatted independently of culture.
- **Cancellation by the caller is always expected.** The client dropped the request and nothing failed; an `Error`
  with a stack trace for every dropped connection is noise. Cancellation not requested by the caller (a timeout
  inside the handler) stays unexpected.
- **Failures are logged in an exception filter** that returns `false`, so the exception is never caught and its stack
  trace is untouched. An exception thrown by `IsExpected` or by a logging provider is swallowed by the filter and
  never replaces the original. The success log is outside the `try`, and a failing logging provider or diagnostics
  listener after success does not turn a completed (possibly saved) operation into an error.
- **The exception type is formatted on every failure**, also with the level off: it is the failure path only, and
  for a non-generic type it is `FullName`.
- **`UnexpectedFailureLevel = LogLevel.None`** for applications that log errors elsewhere (an exception filter), so
  they are not logged twice.

## Domain events

- **No marker interface** - events are application types. `IDomainEventPublisher.Publish` takes `object`.
- **Dispatch by exact runtime type.** A handler of a base type does not receive a subtype: predictability over
  cleverness. The Microsoft DI container applies no variance in `IEnumerable<>`, so `in TEvent` does not change that.
  A handler of an interface or abstract type fails the start-up validation. Handling all events of a kind is an open
  generic handler constrained to that kind, registered by hand as `IDomainEventHandler<>`: the container closes it
  for an event type that satisfies the constraint, skips it in `IEnumerable<>` for the others, and keeps it in
  registration order among the closed handlers. The scan skips open generics, so the application decides the order
  and the lifetime; Pheme adds no API for it.
- **Events and handlers one after another, first exception stops.** Publishing happens inside the save, before it
  writes: the exception has to stop the save, not be swallowed on the way.
- **Handler order:** handlers registered by hand before `AddPheme` first, then scanned ones by full type name. When
  the order matters, register them by hand. The same handler class registered twice handles an event once.
- **`Publish(IEnumerable<object>)` copies the list first**, so a handler may add events to the entity the list came
  from; added events are not published by that call (a further round is the caller's job, done by the EF Core
  interceptor). A collection passed as a single event throws: `List<StructEvent>` is not `IEnumerable<object>` and
  would otherwise be published as an event of type `List<T>` with no handlers.
- **No order attribute on handlers.** Registration by hand covers the need (the example does it); an attribute would
  be a second mechanism next to registration, and the order would be scattered over the handler classes.
- **No handlers "after commit".** A handler run in memory after the commit is lost when the process stops in between
  and has no retries, and inside a transaction of the application `SavedChanges` is not the commit yet. An effect that
  must follow the commit (an e-mail, a call to another system) is an integration event through the outbox, also within
  one module.
- **`IDomainEventPublisher` is public**, although the example publishes only through EF Core: the interceptor of
  `Pheme.EntityFrameworkCore` needs it without `InternalsVisibleTo`, and it is the extension point for saving without
  EF Core (Dapper, Marten). It has no use case in the example - one would be artificial -, an exception to the rule of
  the example; `Pheme.UnitTests` covers it.
- **Integration events do not go through `IDomainEventPublisher`.** Their handlers run all and collect failures, and
  their diagnostics differ; they have their own contracts, with the outbox.

## EF Core dispatch (`Pheme.EntityFrameworkCore`)

- **An interceptor instead of a base `DbContext`.** A dispatching base class takes the context's inheritance and
  needs test doubles for the publisher; with the interceptor the `DbContext` does not depend on the publisher. Hence
  no no-op `IDomainEventPublisher` in Pheme: design-time factories and repository tests need one only when a base
  class takes the publisher.
- **Depends on `Pheme.Abstractions` only** (it lives in the infrastructure layer) and on
  `Microsoft.EntityFrameworkCore` and its `Relational` package - the outbox table needs a table name and schema.
- **One call per `DbContext`: `AddPhemeDbContext<TContext>(...)`.** It registers the services and adds the interceptor
  through EF Core's `ConfigureDbContext<TContext>`, so `AddDbContext` is untouched and its order does not matter. It
  does not register the context itself; it configures what Pheme does with it. Named after the context, not the feature
  (such as `AddPhemeDomainEventDispatch`): everything Pheme does with a context is configured in one place, and the
  members of `PhemeDbContextOptions` name their feature (`CollectDomainEventsFrom`, `MaxDomainEventRounds`). Rejected: a
  registration plus a separate opt-in on `DbContextOptionsBuilder` (`Use...(serviceProvider)`) - forgetting the second
  step was not detectable and silently published nothing; one call cannot be done halfway. A context without the call
  publishes nothing (reports, migrations), deliberately. Only a context that collects domain events gets the
  interceptor, once, also when a later call adds the first collector: a context only with an outbox pays nothing per
  save and needs no publisher.
- **The configuration is applied by the call**, on one `PhemeDbContextOptions` instance per context stored in the
  service collection, not in named options read when the context is first created: `UseOutbox<TModule>()` decides which
  services to register, which lazy options cannot. The delegate gets no services, so applying it early changes nothing
  else; a mistake in it throws from `AddPhemeDbContext`.
- **Entities through delegates, per context:** `CollectDomainEventsFrom<TEntity>(get, clear)` in the options of the
  context, so each module configures its own `DbContext` and contracts of one module never apply to another. Calls for
  the same context add up; the same `TEntity` again replaces the delegates. An entity matching two different registered
  types throws, even without events - its events would be published twice. A `TEntity` that no entity type of the
  context's model is assignable to fails each save, also without events: a contract of another module, or one
  the entities do not implement, would otherwise silently publish nothing. Checked at the first save, not on start -
  the model needs a context instance; once it passes, not again for the registration.
- **Two delegates, `get` and `clear`**, not one "take the events": they fit the usual shape of an entity
  (`DomainEvents` and `ClearDomainEvents()`) and the order "copy, clear, publish"; one delegate would need a method in
  the domain for Pheme.
- **Order:** entries of `ChangeTracker.Entries()` (EF groups them by state), inside an entity the order of its list.
  Events are cleared before publishing, so after a handler exception they do not come back to the entity. Rejected:
  clearing after publishing (a save repeated after the exception would publish to every handler again) and restoring
  the events after an exception (a third delegate, and the repeated save would still store what the handlers changed
  before the exception). After an exception from a save the context - its scope - is discarded, not saved again, as
  with EF Core in general.
- **Changes are detected in every round** (`ChangeTracker.Entries()`; +1.8 ms per save with 5,000 tracked entities):
  handlers add entities through navigations, and switching `AutoDetectChangesEnabled` off around EF Core's own save is
  fragile. A save suppressed by another interceptor (`result.HasResult`) still publishes - there is no scenario for
  anything else.
- **Further rounds** for events added by handlers, up to `MaxDomainEventRounds` (10), then
  `InvalidOperationException` - a cycle of handlers.
- **Synchronous `SaveChanges` throws only when there are events** - saving without events (seeding, data migrations)
  works; with events it would silently skip them.
- **The interceptor is scoped** (it depends on the scoped publisher), one closed generic type per context, resolved
  from the scope the context's options are built in. That needs the default scoped options lifetime. A context that
  collects events fails the start when the last registration of its `DbContextOptions<TContext>` or
  `IDbContextFactory<TContext>` in the final collection is not scoped: pooling and the default singleton
  `AddDbContextFactory` build the options once, from the root, and the interceptor would come from there - with
  `ValidateScopes` (Development) a DI error about the internal interceptor, without it (Production) handlers silently
  getting the root's `DbContext`. The factory is checked too, since `AddDbContext` before `AddDbContextFactory` keeps
  the scoped options (`TryAdd`) while the singleton factory still takes them from the root. Transient options are
  rejected as well: a transient factory or context resolved by a singleton builds them from the root. Without a host,
  building the options runs the same check (the interceptor resolves the validated options first), instead of the
  container's error. A missing `AddPheme` gives a readable `InvalidOperationException` naming the context, not a DI
  error about an internal type.
- **A handler saving the publishing context fails the save.** A handler that calls `SaveChanges` of the context whose
  save publishes its event - itself or through a command whose handler saves the same scoped context - would make EF
  Core commit the entities and changes so far in a transaction of their own, and an exception of a later handler would
  no longer roll them back (measured on SQLite: both rows stayed). Handlers run after the save
  could do this harmlessly. The scoped interceptor remembers the context it is publishing for (restored
  in `finally`, so a failed publication leaves nothing behind) and throws `InvalidOperationException` from
  `SavingChanges` and `SavingChangesAsync` of that same instance, compared by reference: another context, also another
  instance of the same type built from the same scoped options (and so sharing the interceptor), saves normally. It
  throws also inside a transaction the application began, where the nested save would not commit on its own: one rule,
  and the handler's save is never needed there either.
  Rejected: an `AsyncLocal` - it would flow into work a handler starts without awaiting it and fail a save made after
  the publication ended.
- **What is atomic.** The interceptor runs in `SavingChangesAsync`, before EF Core opens the save's own transaction:
  only the changes tracked by the context are atomic with the entity, and the handlers' reads are not isolated
  (hence the concurrency tokens of the example). `ExecuteUpdate`, `ExecuteDelete` and SQL write at once, outside the
  save; a command needing them, or isolated reads, opens a transaction around its work - a behavior of the
  application, the example's `TransactionBehavior`, for the commands that ask for it - and the save and its handlers
  run inside it. Pheme opens no transaction itself: most commands only change tracked entities and need none, and the
  isolation level and the execution strategy are the application's choice. EF Core's retries (`EnableRetryOnFailure`)
  run inside the save, after the interceptor, so they never repeat the handlers.
- **Only dispatch.** Mapping save exceptions (for example concurrency conflicts to 409) stays in the application; the
  example's `ApiExceptionFilter` shows it, with row versions on the entities its handlers read and change.

## Outbox

- **No packages of its own, layered like domain events.** The contracts, of the application and of a store, are in
  `Pheme.Abstractions`; the delivery (`AddOutbox`, the processor, maintenance, the health check) in `Pheme`; the table,
  writer and store in `Pheme.EntityFrameworkCore`. Only the test helpers are a package of their own, `Pheme.Testing`. A
  package is still a layer; the infrastructure layer gets `Pheme` only to register the module's
  handlers ("A module registers its own handlers"). The costs: `Pheme` depends on
  `Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Diagnostics.HealthChecks` (both in the shared
  framework of ASP.NET Core), `Pheme.EntityFrameworkCore` on the `Relational` package, and the application layer can
  see the store contracts - `IOutboxStore` and `IOutboxMessageFactory` are `EditorBrowsable(Advanced)`, and an
  architecture test comparing assemblies cannot tell (`PhemeLayerRule` looks for them in type signatures, see "Layer
  rules"). Rejected: a `Pheme.Outbox` package with the store contracts (it depends on `Pheme` for `AddOutbox`, so the
  EF Core store would depend on the delivery); the store contracts in a package of their own (a
  sixth package, while part of the outbox stays in `Pheme.Abstractions`); a `Pheme.Outbox.*` family (an application
  layer with an outbox references two packages, unlike domain events).
- **Nothing to cut from the configuration.** `AddOutbox()` (composition root), `UseOutbox<TModule>()` (infrastructure,
  without `Pheme`), `modelBuilder.AddPhemeOutbox()` (visible in the migrations), the module marker (application) and
  the optional `AddPhemeOutboxCheck` each belong to another layer, and a missing one fails the start or the first use
  with a hint.
- **`IIntegrationEvent` is a marker, unlike domain events.** Integration events are messages between modules, not
  types of the domain, and the outbox needs their `EventId` and `OccurredOn`.
- **A writer addressed by a module marker**, `IOutboxWriter<TModule>`: a handler of the application layer writes to the
  outbox of its module without seeing the `DbContext` and without an interface per module. Rejected:
  `IIntegrationEventPublisher.Publish(event)` choosing the outbox by the context that saves - in a scope with two
  contexts the event could join the transaction of another module, silently without the guarantee.
- **Integration events only through the outbox.** Handlers without `AddOutbox` fail the start instead of being called
  in-process: that would lose the guarantee of the outbox - written with the change, delivered once it is saved - and
  reacting in-process is what domain events are for.
- **Event types come from the handlers** (the `TEvent` of each registered `IIntegrationEventHandler<TEvent>`), without
  a registration of contract assemblies: only an event with a handler needs a type to be read into, and there is no
  "forgot to register the contracts" error to make.
- **The stored name** is `[IntegrationEventName]`, otherwise the full name of the runtime type, so a type can move to
  another namespace or assembly when it has the attribute. The attribute is not inherited - a subtype would share the
  name of its base, a duplicate. Its constructor does not throw (an attribute's exception appears only when it is read
  by reflection); an empty or duplicate name fails the start instead. **Generic event types are rejected**: the full
  name of a closed generic type carries the assembly versions of its arguments, and one attribute on the definition
  would name every construction alike. Allowing them later breaks nothing.
- **JSON with the web defaults, not configurable**: one `JsonSerializerOptions` for writing and reading. Reading is
  case-insensitive, so PascalCase content (`JsonSerializer` with its default options) is read too. An option for
  converters can come in a minor version without a breaking change.
  Strict reading (`RespectRequiredConstructorParameters`, `RespectNullableAnnotations`) is rejected: rows written before
  a property was added would be parked instead of read with its default.
- **`OccurredOnUtc` is `OccurredOn` converted to UTC**: Npgsql writes only an offset of 0 to `timestamptz`.
  `TraceParent` is set only for a W3C activity; a hierarchical `Activity.Id` is not a traceparent.
- **The outbox table belongs to Pheme** (`modelBuilder.AddPhemeOutbox()`): one schema to document and migrate, mapped
  explicitly in `OnModelCreating` rather than by an `IModelCustomizer`, so the table shows in the model and the
  migrations. `outbox_messages` in the default schema of the context; the column names come from the model, so an
  application's naming convention applies. The key is the event's `EventId`, never generated: the same event added twice
  fails (the change tracker of one context, the key across saves) instead of being delivered twice. The index
  (`ProcessedOnUtc`, `AttemptCount`, `OccurredOnUtc`, `NextAttemptOnUtc`) serves the query of the due messages;
  `NextAttemptOnUtc` is a filter there, so it comes last.
- **A writer per module, bound to one context.** `UseOutbox<TModule>()` registers a scoped
  `IOutboxWriter<TModule>` that adds the message to the context of its scope, without saving: the context's save writes
  it in the same transaction as the change. Several modules may share a context; a writer never writes to another
  context - another transaction would lose the guarantee.
- **The message format is Pheme's** (`IOutboxMessageFactory`), so the EF writer only adds an entity; the store's
  `source` defaults to the context's name without `DbContext` (`OrderingDbContext` → `Ordering`), `OutboxSource`
  sets another (two contexts of one name).
- **A missing half fails early.** `UseOutbox` without `AddOutbox` fails the start (checking the factory with
  `IServiceProviderIsService`), or else the first resolution of a writer or a store. A model without
  `AddPhemeOutbox()` fails the first `Add` and the first use of the store: checking it at start would create the
  context, which may need services of a request.
- **One store per context, unique modules and sources.** `UseOutbox` registers the context's `EfOutboxStore` once,
  whatever the number of modules, so a context never has two stores. One `ValidateOnStart` checks all contexts with an
  outbox together: a module in two contexts (its writer would write to the last one registered) and a source in two
  contexts (the processor saves an outcome to the store of the message's source, and maintenance addresses stores by
  source) fail the start. Without a host the writer and the store read the same validation when they are resolved, so
  a test application gets the error too.
- **The EF store sees only its source.** It reads and updates the rows whose `Source` is its own, so contexts mapping
  one table (two contexts in one default schema) do not deliver each other's messages. Reads are `AsNoTracking`, updates
  and deletions single statements (`ExecuteUpdate`, `ExecuteDelete`): nothing else tracked by the store's context is
  saved with a status, and the domain event interceptor does not run. `MarkAsFailed` increments in SQL and reads the
  count back - two statements, enough with one processor. `ProcessedOnUtc` comes from the registered `TimeProvider`, and
  the times given to `DeleteProcessedBefore`, `GetPending` and `MarkAsFailed` are converted to UTC, since Npgsql writes
  only an offset of 0 to `timestamptz`.
- **Supported databases: a list, detected from the provider.** A database is supported when the outbox tests pass on
  it; they run on PostgreSQL, so only PostgreSQL is. `OutboxDatabases` lists the supported EF Core providers,
  and the writer and the store check the context's `Database.ProviderName` against it on first use, failing with an
  error that names the provider - SQLite, for example, cannot order, compare or aggregate `DateTimeOffset`, and would
  fail deep in EF Core instead. Detected, not configured: an option (`UsePostgreSql()`, an enum) would repeat what the
  context already says and could disagree with it. The queries are EF Core LINQ only (`ExecuteUpdate`,
  `ExecuteDelete`, no raw SQL, no provider package), so another database is an entry in the list plus a run of the
  shared outbox tests on it (`docs/development.md`, "Adding a database"), no new API. When a database needs SQL of its
  own (row locks for several processor instances, say), the entry's value becomes the type that carries it, chosen by
  the same detection; a package per database only once one needs a reference to the provider. Storage other than EF
  Core implements `IOutboxStore` (`Pheme.Abstractions`).
- **A processor and a runner.** `OutboxProcessor` (a `BackgroundService` added by `AddOutbox`) only loops and waits;
  one cycle is `IOutboxRunner.RunOnce`, public for tests and own schedules (`ProcessInBackground = false`). Runs of one
  provider never overlap (a semaphore in the runner), so tests sharing an application cannot deliver a message twice
  at the same time; two application instances still can - the stores do not lock rows, documented as a limitation.
  Locking is postponed until an application needs several instances: `FOR UPDATE SKIP LOCKED` would need a
  transaction held while the handlers run, or raw SQL; a lease on a due-time column would need neither.
- **Sequential delivery**, stores and messages one after another: it keeps the order and the code simple.
- **A scope per handler, the outcome in another new scope.** Rejected: one scope per cycle - tracked entities of a
  failed handler would be saved by the next message's handler or by the status update; a scope per message - the next
  handler of the same event would save the work of a failed one when both change the same scoped `DbContext` (measured:
  both rows saved, and the failed handler did its work again on the retry). `CompletedHandlers` assumes each handler
  saves on its own, so each gets its own scope. The handler classes are read from a scope of their own first, resolving
  the registered handlers - a registration by hand may use a factory, so the descriptors do not always name the class -,
  then each handler is resolved again in its scope and picked by its class. Resolving every handler once more per
  handler costs constructors, not I/O, at the usual one to three handlers per event. Batches are read in a short scope
  of their own.
- **Completed handlers are an opaque string for the store.** `MarkAsFailed` takes the formatted
  `OutboxMessage.CompletedHandlers` (one handler type name per line - generic names contain `", "`) instead of a list,
  so the format lives only in Pheme and a store just saves it. A handler is identified by its class name in C#
  notation for generic types, with the `+` of a nested type kept (see "Diagnostics"), without assembly versions: a
  renamed handler class runs again, which at-least-once delivery allows.
- **A growing delay between attempts, stored with the message.** A failed message waits `RetryDelay` (10 s), doubled
  after each further failure up to `MaxRetryDelay` (15 min), and `MaxAttempts` is 10: about 50 minutes of failures
  before parking, instead of about 20 seconds with a retry at each poll (`PollingInterval` 5 s, 5 attempts). The values
  are chosen for integration event handlers that mostly send e-mails, so the first retries come within
  seconds of a short outage, and a message is at most 15 minutes late once the mail server works again. The due time is
  a column, `NextAttemptOnUtc` (null: due at once), compared in `GetPending` with the `now` of the runner's
  `TimeProvider`, passed in so the store needs no clock and the due time and the comparison come from one clock; the
  runner computes it from the attempts read with the message (one processor, see "A processor and a runner") and
  passes null for the attempt that parks the message. `Requeue` and `Redeliver` clear it. The delay is computed in
  `double`, with at most 64 doublings, so no number of attempts overflows. No jitter: delivery is sequential in one
  instance, so failed messages do not retry in a burst against the same service.
- **A failure does not pause the drain.** Rejected: waiting `PollingInterval` after any failure, also with a full
  batch, so that a failed message back at the head of the next batch does not use up its attempts in milliseconds -
  one poison message in each batch would throttle the whole backlog to a batch per interval. A message
  before its due time is not read at all, so the processor goes on at once after a full batch. The pause stays only for
  `RetryDelay = TimeSpan.Zero` (due at once, as `UsePhemeOutboxForTests` sets it). A run that throws (a store that
  cannot be read) is logged and repeated after `PollingInterval`; it does not stop the processor.
- **A stable order for messages of one moment.** `GetPending`, `GetParked` and `GetByType` order by `OccurredOnUtc`,
  then `Id`: the events of one transaction often share their time, and without a second key their order depended on
  the query plan. With `Guid.CreateVersion7()` (the example) the `Id` follows creation; with a random `Guid` the order
  is at least stable. Part of the store contract, and the merge of several stores (maintenance, `GetOutboxEvents`)
  orders the same way. PostgreSQL compares `uuid` as the .NET `Guid` does, in the order of its text.
- **An event without a handler is rejected when written.** `IOutboxMessageFactory.Create` - so `IOutboxWriter.Add` -
  throws `InvalidOperationException` when no handler of the event's exact runtime type is registered (the stored name
  maps to another type or to none): in a modular monolith the writer and the handlers are one application, so such an
  event is a mistake, and the outbox would park it after all its attempts, in production. Failing in the request that
  writes it shows it in the first test. The check reads the same `IntegrationEventTypes` as the dispatcher.
- **Every failure counts as an attempt**, also one that cannot succeed (an unknown stored name - since the write
  rejects events without a handler, only a renamed type or a handler removed after the write -, a `JsonException`):
  one rule, `MaxAttempts` attempts, is simpler to describe and to test than parking some failures at once. A message
  whose name no handled type has is never marked as processed either: the processor cannot tell a type without a
  handler from one renamed without `[IntegrationEventName]`, and for the second it would lose data.
- **Shutdown does not count as an attempt.** A delivery that fails once the token is cancelled is not recorded and the
  run ends with `OperationCanceledException`; the message comes again after the restart. An outcome is saved with
  `CancellationToken.None`: the handlers have done their work, and a lost outcome would repeat them.
- **`Error` is `Exception.ToString()`**, with the stack trace, for inspecting a parked message; an
  `AggregateException` of a single failed handler is unwrapped (also for the logs and `error.type`).
- **Logs only for failures**, in the category `Pheme.Outbox`: a warning per failed attempt, an error when a message is
  parked or a run fails. Like request logging, it requires `ILoggerFactory` from DI without a fallback.
- **Time from `TimeProvider`** (registered with `TryAdd`, `TimeProvider.System` by default), so tests drive the polling
  with a fake clock.
- **Maintenance on the stores of the scope.** `IOutboxMaintenance` is scoped and uses the `IOutboxStore`s of its scope,
  like any service of an endpoint. Requeuing all parked messages pages through `GetParked` and calls `Requeue` for
  each instead of a bulk store method: maintenance is rare, and every store method is work for a custom store. A
  requeue resets the attempts, the error and the due time (due at once) but keeps the completed handlers - after a fix,
  the handlers that worked must not run twice. An unknown `source` throws instead of requeuing nothing.
- **Retention in the processor**, on start and then every hour (a constant: nobody needs another rhythm for deleting
  history), only while `ProcessInBackground` is on - tests keep everything. A store whose deletion fails is logged and
  does not stop the others or the delivery. `ProcessedRetention = null` keeps all history.
- **Every deletion is logged, at `Information` by default** (`OutboxOptions.RetentionLogLevel`, `None` turns it off):
  history disappearing from a table should leave a trace, the outbox and the count are enough to see it, and a
  level option is the same knob as in request logging. It is logged also when nothing was deleted (hourly), so the next
  due time is always visible; a quieter level is for those who do not want that.
- **The store reports the oldest pending time, not an age** (`OutboxStats.OldestPendingOccurredOnUtc`), so the stats
  need no clock in the store; the health check and the gauge compute the age with `TimeProvider`.
- **Health check on `IHealthChecksBuilder`** (`AddPhemeOutboxCheck`, like `AddDbContextCheck`), which is in
  `Microsoft.Extensions.Diagnostics.HealthChecks`, not in its `.Abstractions`. A parked message reports the
  registration's failure status (`Unhealthy` unless given): it needs a person - also a store that cannot be read is
  `Unhealthy`, so the default stays. It belongs to monitoring and alerts, not to the probes of the host: a parked
  message is fixed neither by a restart nor by taking the instance out of traffic, and with one instance a readiness
  probe would stop the whole API for one message. The example maps it to an endpoint of its own; an application that
  must have it in readiness lowers it to `Degraded`, which ASP.NET Core answers with 200. An old pending message is
  `Degraded`: the delivery is behind or stuck, but works; a message waiting for its next attempt counts as pending.
- **One index, no `Source` in it, no partial index, retention in one statement**: one store per table is the usual case,
  and thirty days of moderate traffic are thousands of rows. To revisit when `pheme.outbox.messages` or the time of the
  deletion shows it.
- **Gauges read on collection.** `pheme.outbox.messages` and `pheme.outbox.oldest_pending.age` are observable: the
  stores are read only when a listener collects, so there is no cost without one. Observable callbacks are
  synchronous, so the store is called synchronously over its async method - acceptable for a collection every few
  seconds without a synchronization context. A store that cannot be read is left out of the collection instead of
  failing it. The gauges are created with the runner, once per provider.
- **Test helpers in `Pheme.Testing`.** Every application wrote its own "drainer"; `DrainOutbox` runs
  `IOutboxRunner.RunOnce` until a run delivers nothing (the events handlers write are delivered too) and throws for a
  failed message - once per message and provider: tests often share one application and its outbox, and a message
  that failed in one test is retried in the next, which must not fail because of it. It requires
  `UsePhemeOutboxForTests` (processor off): a running processor would deliver messages and their failures would go
  unnoticed. A run limit (10 by default) stops an endless chain of handlers writing events. `UsePhemeOutboxForTests`
  also sets `RetryDelay` to zero, so the next drain retries a failed message as before the growing delay: a test that
  repairs a fake and drains again needs no clock. Rejected: a fake `TimeProvider` advanced by the drain - it would
  change the time of the whole application under test.
- **Test reads through the store contract.** `GetOutboxEvents` and `Redeliver` use two `IOutboxStore` methods,
  `GetByType` and `Redeliver`, instead of reading the EF Core tables: the helpers work with any store, and the rules of
  the store (its source, the database check) stay in one place. The cost is two more methods for a custom store; neither
  can do more harm than `MarkAsProcessed` or `DeleteProcessedBefore`, already on the contract. Only processed events
  are redelivered, with the completed handlers cleared, so all handlers run again - a test of idempotency. The stored
  name (`[IntegrationEventName]`, otherwise the full name) and the JSON (web defaults) are repeated in `Pheme.Testing`
  instead of shared through `InternalsVisibleTo` (see "Extension packages"): they are the format of the messages
  already in the database, which cannot change without breaking them.

## Layer rules (`Pheme.Testing`)

- **The rules of [Layers](guide/10-layers.md) as API.** Otherwise each application copies the example's layer tests, and
  comparing assemblies misses the store contracts, which are in `Pheme.Abstractions`. `PhemeLayerRule` has one static
  instance per row of the table (`Domain`, `Contracts`, `Application`, `Infrastructure`), no public constructor and no
  options: the table of that chapter is the rule set. Every `Pheme.*` assembly a rule does not list is forbidden, so a
  package added later stays forbidden until a release of Pheme allows it. The composition root may use everything, so it
  has no rule. The store contracts (`IOutboxStore`, `IOutboxMessageFactory`, `OutboxMessage`, `OutboxStats`) are
  forbidden in the contracts as well as in the application: a message between modules has no use for them either.
- **The infrastructure may use `Pheme`**, to register the module's handlers ("A module registers its own
  handlers"). The rule cannot tell `AddPheme` with the module's assembly from `AddBehavior` or `AddOutbox` in a method
  body; a behavior added by a module wraps the messages of every module, so the guide keeps the behaviors and the
  outbox in the composition root. `Pheme.FluentValidation` stays forbidden: the composition root's
  `AddFluentValidation` already covers the assemblies of the modules' calls.
- **Two layouts, one signature walk.** A layer that is an assembly is checked by its referenced Pheme assemblies - the
  compiler keeps a reference only when the code uses it, method bodies included - and by the store contracts in its type
  signatures. A layer that is a namespace (with the namespaces inside it) is checked by the signatures alone: base
  types, interfaces, fields, properties, events, parameters and return types, with generic arguments and element types.
  Accessors and the fields the compiler generates for properties, events and primary constructor parameters are
  reported as their member, once. Types the compiler generates from bodies (async state machines, closures) are types
  of the namespace and are checked; bodies, attributes and generic constraints are not. A namespace without types is an
  `ArgumentException`: a renamed namespace would otherwise pass with nothing checked.
- **No test framework.** A check throws one `InvalidOperationException` with every violation, sorted, as `DrainOutbox`
  reports every failed message: a test sees all of them in one run, under any framework. `ToString` returns the layer's
  name, so a theory over the rules shows which one failed.

## Diagnostics

- **`ActivitySource` and `Meter` named `Pheme`**, from the BCL without extra packages, enabled with ordinary
  OpenTelemetry configuration. Pheme itself does not log outside request logging; spans are enough for correlation.
- **OpenTelemetry conventions** instead of a custom result tag and an error counter: a failure is the tag
  `error.type` (full exception type name) plus span status `Error` with the exception message; success has no tag and
  status `Unset`. The error count is the histogram's count with `error.type` - one instrument less, dashboards as for
  ASP.NET Core metrics. No `exception` event with a stack trace on the span - logging handles that. The status
  description is the exception message: the conventions allow it, and request logging writes the same message.
- **Every exception thrown to the caller is a failure**, cancellation included (unlike request logging): the span
  describes the outcome, it does not judge it.
- **Type names as written in C#** (`TypeNames`) in logs, span names, `pheme.message.type` and error messages: generic
  arguments in angle brackets, each on its own type of a nesting chain (`Ns.Outer<A>.Inner<B>`), and a dot for a
  nested type (`Shop.PlaceOrder.Command`, the vertical slice style) instead of the `+` of `Type.FullName`. Names stored
  as data keep the `+`: the full name of an integration event without `[IntegrationEventName]` and the completed
  handlers of an outbox message, where `Ns.Outer+Inner` must not equal the type `Inner` of the namespace `Ns.Outer`.
- **Span name** is the full type name, `ActivityKind.Internal`. The span and the measurement cover the whole pipeline
  and handler; for domain events, all handlers of one event, one span per event, also without handlers.
- **An outbox delivery** is a span named after the stored event name (the name that identifies it across versions)
  with `ActivityKind.Consumer`, as messaging conventions have it, and the tag `pheme.outbox.source`. It is not a child
  of the writer: the writer's trace ended long ago, and one trace per request stays readable; a link to the
  `TraceParent` of the message connects them instead. `pheme.outbox.source` is not on the histogram - a tag per store
  multiplies its series for little gain.
- **`pheme.message.duration` in seconds** with the bucket boundaries of ASP.NET Core (`InstrumentAdvice`); OTel's
  default buckets are for milliseconds. The same histogram covers domain events (`domain_event`) and outbox deliveries
  (`integration_event`).
- **`Meter` from `IMeterFactory`** in DI, so metrics of separate providers (tests, `WebApplicationFactory`) do not mix;
  without a factory Pheme creates a meter per provider and disposes it with the provider. The `ActivitySource` is
  static (the BCL has no factory for it).
- **No cost without a listener:** no `Activity` is created, the histogram records nothing, the exception type is
  formatted only when someone listens.
- **Known gap:** a listener that throws when an activity *starts* still breaks `Send`; there is no clean way to guard
  it, because the activity stays current.

## Dependencies

- **Lowest version of the .NET 10 band** (`Microsoft.Extensions.*` 10.0.0, `Microsoft.EntityFrameworkCore` and its
  `Relational` 10.0.0, `FluentValidation` 12.0.0): the application chooses newer patches.
