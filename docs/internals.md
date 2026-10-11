# How Pheme works inside

How the code does what the [documentation](README.md) promises. The reasons behind each choice are in
[`design.md`](design.md); this file follows the code paths, class by class, so you can read it with the source open.
It describes the current state of `src/`; update it with the code.

## The big picture in one screen

Pheme does its work at three moments, and the outbox at a fourth:

```
REGISTRATION (once, while Program.cs builds the IServiceCollection)
  AddPheme(options => ...)
    ├─ PhemeOptions records what you asked for (assemblies, behaviors, logging)
    ├─ HandlerRegistry scans the assemblies and puts handlers into DI
    ├─ PipelineBehaviors remembers the behaviors, in order
    └─ a validator is hooked into Microsoft.Extensions.Options

STARTUP (once, when the host starts or the first ISender is created)
  HandlerRegistrationValidator checks the registrations: one handler per message, and so on

CALL TIME (every Send / Publish / SaveChangesAsync)
  Sender / DomainEventPublisher
    ├─ take a wrapper for the message type from a static cache (built by reflection the first time)
    └─ the wrapper runs diagnostics → behaviors → handler, all strongly typed

BACKGROUND (outbox, while the host runs)
  OutboxProcessor → OutboxRunner: due messages of every store, each handler called in a scope of its own
```

Everything below explains one of these boxes.

### Where state lives

The most confusing part of the code is that state lives at four different levels. Keep this table nearby.

| Level | What | Why there |
|---|---|---|
| **Static** (whole process) | wrapper caches in `Sender`, `DomainEventPublisher` and `IntegrationEventDispatcher`, `ActivitySource`, `static readonly` names in generic classes | a wrapper depends only on the message type, so one per type is enough for every provider |
| **Per `IServiceCollection`** | `HandlerRegistry`, `PipelineBehaviors`, `FluentValidationSettings`, `DbContextRegistration<TContext>` | many `AddPheme` (or `AddPhemeDbContext`) calls must share them; stored *inside* the collection as singleton instances |
| **Per service provider** (singleton) | `MessageInstrumentation`, `RequestLoggingBehavior<,>`, options and their cached validation, the outbox's `IntegrationEventTypes`, `IntegrationEventDispatcher`, `OutboxRunner`, `OutboxProcessor` | one per built application; tests and `WebApplicationFactory` build many |
| **Per scope** (one request) | `Sender`, `DomainEventPublisher`, `DomainEventDispatchInterceptor<TContext>`, `EfOutboxWriter<TContext, TModule>`, `OutboxMaintenance`, handlers and behaviors (transient, created in the scope) | they resolve handlers and the `DbContext` of the current request |

## 1. Registration: `AddPheme`

Entry point: [`PhemeServiceCollectionExtensions.AddPheme`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/PhemeServiceCollectionExtensions.cs).

```csharp
var options = new PhemeOptions(services);
configure(options);                                   // 1.1  your lambda only records

HandlerRegistry registry = HandlerRegistry.GetOrAdd(services);   // 1.2  shared state + validation hook
services.AddPhemeCore();                              // 1.5  ISender, instrumentation
services.TryAddScoped<IDomainEventPublisher, DomainEventPublisher>();
registry.Register(options);                           // 1.3  scan and register handlers
PipelineBehaviors behaviors = PipelineBehaviors.GetOrAdd(services);
behaviors.Add(options.Behaviors);                     // 1.6  behaviors, in order
if (options.RequestLogging)
{
    behaviors.AddRequestLogging();
}
```

### 1.1 `PhemeOptions` is a recorder

[`PhemeOptions`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/PhemeOptions.cs) is created fresh for each `AddPheme` call. Its methods only add to
private lists: `AddHandlersFromAssemblies` to `Assemblies`, `AddBehavior` to `Behaviors`, `AddAssemblyScanner` to
`AssemblyScanners`, and `AddRequestLogging` sets a flag. Nothing is scanned while your lambda runs. `AddPheme` reads
the lists after the lambda returns.

Two exceptions do touch the collection right away:

- `AddRequestLogging(configure)` registers `services.AddOptions<RequestLoggingOptions>().Configure(configure)`. Each
  call adds one more configure action, so several calls configure one options instance in call order.
- `AddBehavior` validates the type at once (`BehaviorTypes.Validate`), so a wrong type fails where it was written.

`PhemeOptions` is not `IOptions<PhemeOptions>`: it belongs to one call.

### 1.2 State stored inside the `IServiceCollection`

`AddPheme` may run once per module, and the calls must share their state: one list of assemblies, one pipeline.
`IServiceCollection` is only a list of `ServiceDescriptor`s, with no place for extra data. So Pheme puts its state
object *into* the list, as a singleton instance, and finds it again on the next call:

```csharp
// HandlerRegistry.GetOrAdd (PipelineBehaviors and FluentValidationSettings do the same)
if (CollectionState.Find<HandlerRegistry>(services, registry => registry.Services) is { } existing)
{
    return existing;                         // second AddPheme: reuse
}

var registry = new HandlerRegistry(services);
services.AddSingleton(registry);             // first AddPheme: store the instance in the collection
```

[`CollectionState.Find`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Internal/CollectionState.cs) looks for a descriptor whose `ServiceType` is the
state type and takes its `ImplementationInstance`. It also checks that the instance was created for *this* collection
(`ReferenceEquals(owner(state), services)`). If someone copied descriptors from another collection, the state would
point at the old one and every later registration would silently go there, so `Find` throws instead.

That singleton registration also works later, at run time: the same object is resolved from DI. `Sender` gets
`PipelineBehaviors` injected, and `HandlerRegistrationValidator` gets `HandlerRegistry`. This is how call-time code
reaches what registration recorded.

`GetOrAdd` of `HandlerRegistry` also installs the startup validation hook, once (section 2).

### 1.3 Scanning: `HandlerRegistry.Register`

[`HandlerRegistry`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Registration/HandlerRegistry.cs) holds every assembly of every `AddPheme` call, the
concrete message and integration event types found, and the assembly scanners of extensions. `Register(options)` runs
in two steps:

1. **New scanners catch up** (section 1.4).
2. **Each new assembly** is remembered, scanned (`Scan`) and passed to every scanner. An assembly seen before is
   skipped.

`Scan(assembly)`:

```csharp
foreach (Type type in GetTypes(assembly).OrderBy(type => type.FullName, StringComparer.Ordinal))
{
    if (type.IsAbstract || type.ContainsGenericParameters) continue;    // only concrete, closed types
    if (typeof(IMessage).IsAssignableFrom(type)) messageTypes.Add(type); // remembered for the validation
    if (typeof(IIntegrationEvent).IsAssignableFrom(type)) integrationEventTypes.Add(type); // likewise
    if (!type.IsClass) continue;

    foreach (Type serviceType in type.GetInterfaces())
        if (IsHandlerInterface(serviceType) || IsDomainEventHandlerInterface(serviceType)
            || IsIntegrationEventHandlerInterface(serviceType))
            Services.TryAddEnumerable(ServiceDescriptor.Transient(serviceType, type));
}
```

What to notice:

- **Sorting.** `GetTypes()` has no documented order, but DI returns `IEnumerable<T>` in registration order, and that
  is the order event handlers run in. Sorting by full name makes it stable.
- **`messageTypes`** and **`integrationEventTypes`** are not used for registration. They are the lists of messages
  and integration events that must have a handler, read by the validator.
- **One descriptor per interface.** A class implementing `ICommandHandler<A>` and `ICommandHandler<B>` is registered
  twice, once under each interface.
- **`TryAddEnumerable`** adds a descriptor unless one with the same service type *and* the same implementation type
  is already there. So a handler registered by hand before is not added again, and keeps its lifetime, while a
  *different* class for the same interface is added. The validator then reports it as a duplicate (for commands and
  queries) or both run (for domain and integration events). The same class registered by hand *after* `AddPheme` is a
  second descriptor; the validator names it as one class registered twice, not as two handlers.
- **`GetTypes` failure** (`ReflectionTypeLoadException`, usually a missing dependency) becomes an
  `InvalidOperationException` naming the assembly and the first three loader errors.

The recognition of contracts is in [`MessageContracts`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Internal/MessageContracts.cs): which generic
definitions are handler interfaces, which handler interface handles a given message (`GetHandlerInterface`), and
whether a message is a command or a query (`GetMessageKindOf`).

### 1.4 Assembly scanners: how extensions find their types

An extension package (today `Pheme.FluentValidation`) needs to find its own types next to the handlers. It calls
`options.AddAssemblyScanner(scanner)`, where `scanner` is an `Action<IServiceCollection, Assembly>`.
`HandlerRegistry` guarantees that every scanner sees every assembly exactly once, whatever the order of calls:

```
AddPheme #1 (Ordering):  assemblies [Ordering]               scanners []
AddPheme #2 (Shipping):  assemblies [Shipping]               scanners [ValidatorScanner.Scan]
                         → new scanner runs on Ordering      (catch up on earlier assemblies)
                         → Shipping is scanned, then all scanners run on it
AddPheme #3 (Billing):   assemblies [Billing]                scanners []
                         → Billing is scanned, then ValidatorScanner.Scan runs on it (known from #2)
```

Scanners are compared with `Contains`, that is delegate equality: the same static method is one scanner. That is why
the docs ask for a static method rather than a lambda capturing state.

### 1.5 `AddPhemeCore`

[`CoreServiceCollectionExtensions.AddPhemeCore`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Registration/CoreServiceCollectionExtensions.cs)
registers the services every `AddPheme` needs: options support, `PipelineBehaviors`, the `MessageInstrumentation`
singleton and the scoped `ISender`. All of it uses `TryAdd...`, so the second call changes nothing.

### 1.6 Behaviors: `PipelineBehaviors`

[`PipelineBehaviors`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Pipeline/PipelineBehaviors.cs) holds:

- `behaviorTypes`: the behavior types of all calls, as added (open generic like `ValidationBehavior<,>`, or closed);
- `requestLogging`: whether request logging is on;
- `version`: incremented on every change.

`Add(types)` appends the types not seen yet and registers each **as a service of its own type**:
`services.TryAdd(ServiceDescriptor.Transient(behaviorType, behaviorType))`. There is no
`IPipelineBehavior<,>` registration at all. The order lives in this list, not in the DI container. `TryAdd` means an
application can register a behavior itself before `AddPheme` with another lifetime, and Pheme keeps it.

`AddRequestLogging()` only sets the flag and registers `RequestLoggingBehavior<,>` as a singleton of its own type. It
is not put into `behaviorTypes`: `Close` puts it in front on its own (section 3.4), which is why request logging is
always outermost.

Everything takes a `lock`: `Close` may run on a request thread while a test is still adding behaviors to the same
collection.

## 2. Startup validation

Files: [`HandlerRegistrationOptions`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Registration/HandlerRegistrationOptions.cs),
[`HandlerRegistrationValidator`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Registration/HandlerRegistrationValidator.cs).

**The goal.** Check the registrations once the collection is complete, that is after every `AddPheme` call and every
registration made by hand, and before the first request. Run the check once, both with a host (ASP.NET Core) and
without one (a console app or a test with a bare `ServiceProvider`).

**The trick.** `Microsoft.Extensions.Options` already does all of this for options classes. Pheme defines an options
class with no settings and attaches its validator to it:

```csharp
// HandlerRegistry.GetOrAdd, once per collection
services.AddOptions<HandlerRegistrationOptions>().ValidateOnStart();
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IValidateOptions<HandlerRegistrationOptions>, HandlerRegistrationValidator>());
```

`HandlerRegistrationOptions` carries no data. It is a key that says "these validators belong together". What happens
next:

```
With a host                                   Without a host
───────────                                   ──────────────
host.StartAsync()                             provider.GetRequiredService<ISender>()
  └─ ValidateOnStart                            └─ Sender constructor
       └─ IOptionsMonitor<HRO>.Get(...)              └─ IOptionsMonitor<HRO>.CurrentValue
            └─ OptionsFactory creates HRO                 └─ (same monitor, same cache)
                 └─ IValidateOptions<HRO>.Validate
                      └─ HandlerRegistrationValidator: OK → cached
                                                       problems → OptionsValidationException
```

Both paths go through the same `IOptionsMonitor` cache of the provider, so a valid result is computed once. An invalid
one throws on every attempt, so a broken application cannot send anything.

**What the validator reads.** It gets `HandlerRegistry` from DI and reads `registry.Services`, the live
`IServiceCollection`, at validation time. That is why it sees handlers registered by hand after `AddPheme`, and
`RemoveAll` or `Replace` in integration tests.

**What it checks** (`FindProblems`, all problems in one exception):

- each concrete message from the scanned assemblies implements exactly one of `ICommand`, `ICommand<T>`, `IQuery<T>`
  (not only `IMessage`, not two of them);
- its handler interface has a registration;
- no handler interface (scanned or not) has more than one non-keyed registration;
- nobody registered a service as `IPipelineBehavior<,>`, which Pheme would silently ignore;
- no domain event handler handles an interface or abstract event type, which no event's runtime type is (the number
  of handlers is not checked: any number, zero included, is fine);
- integration event handlers ([`IntegrationEventTypes.FindProblems`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/IntegrationEventTypes.cs)):
  the outbox is added (the registration of the internal `IntegrationEventDispatcher` marks it), and each handled event
  type is concrete, not generic, and has a non-empty stored name no other handled type has;
- with the outbox, each concrete integration event from the scanned assemblies has a non-keyed handler registration
  (without the outbox no event can be written, so none is checked).

## 3. Sending: from `ISender.Send` to the handler

### 3.1 `Sender` and its static caches

[`Sender`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Sending/Sender.cs) is scoped. It receives the scope's `IServiceProvider`, the collection's
`PipelineBehaviors` and the provider's `MessageInstrumentation`. Its constructor also reads
`IOptionsMonitor<HandlerRegistrationOptions>.CurrentValue`, which triggers the validation of section 2 when no host did.

`Send` receives the message as an interface (`ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>`). Its concrete
type is known only at run time, but DI needs concrete types such as `ICommandHandler<PlaceOrder, Guid>`. Building them
by reflection on every call would be slow, and a call through `MethodInfo.Invoke` wraps exceptions in
`TargetInvocationException`. So `Sender` builds a **wrapper** per message type once and keeps it:

```csharp
public Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
{
    IMessageHandlerWrapper<TResponse> wrapper = ResponseWrappers<TResponse>.Queries.GetOrAdd(
        query.GetType(),
        static queryType => CreateWrapper<TResponse>(typeof(QueryHandlerWrapper<,>), queryType, typeof(TResponse)));
    return wrapper.Handle(query, serviceProvider, behaviors, instrumentation, cancellationToken);
}
```

Three caches, all `static`:

- `CommandWrappers`: commands without a response, values `IMessageHandlerWrapper<Unit>`;
- `ResponseWrappers<TResponse>.Commands` and `.Queries`: a **static generic class**. The runtime creates a separate
  copy of its static fields for every `TResponse`, so `ResponseWrappers<Guid>.Queries` holds only
  `IMessageHandlerWrapper<Guid>` and needs no cast. A plain `Dictionary<Type, object>` would need one.

`CreateWrapper` is the only reflection: `MakeGenericType` plus `Activator.CreateInstance`, once per message type.

### 3.2 The wrapper classes

```
IMessageHandlerWrapper<TResponse>                     interface; Handle(IMessage, ...): the untyped door
  └─ MessageHandlerWrapper<TMessage, TResponse>       abstract; casts once, diagnostics, builds the pipeline, GetHandler
       ├─ CommandHandlerWrapper<TCommand>              ICommand            → ICommandHandler<TCommand>, returns Unit
       ├─ CommandHandlerWrapper<TCommand, TResponse>   ICommand<TResponse> → ICommandHandler<TCommand, TResponse>
       └─ QueryHandlerWrapper<TQuery, TResponse>       IQuery<TResponse>   → IQueryHandler<TQuery, TResponse>
```

`Sender` sees only the interface, whose `Handle` takes `IMessage`. One interface call lands in the closed generic
class, and from there on everything knows `TMessage`. That is the bridge from "some message" to typed code. The door
is an interface, as in the two event families (`IDomainEventHandlerWrapper`, `IIntegrationEventHandlerWrapper`): it
holds no code, so each implementation keeps a plain name and a file of its own.

A side effect used throughout the code: a `static readonly` field of a generic class exists once per closed type. So
`MessageType` and `MessageKind` in `MessageHandlerWrapper<TMessage, TResponse>` (and in `RequestLoggingBehavior`,
`DomainEventHandlerWrapper<TEvent>`) are computed once per message type, with no dictionary.

### 3.3 `Handle`: diagnostics around everything

[`MessageHandlerWrapper<TMessage, TResponse>.Handle`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Sending/MessageHandlerWrapper.cs):

```csharp
var typedMessage = (TMessage)message;
MessageMeasurement measurement = instrumentation.Start(MessageType, MessageKind);   // span + stopwatch
try
{
    response = await HandlePipeline(typedMessage, serviceProvider, behaviors, cancellationToken);
}
catch (Exception exception)
{
    measurement.Stop(exception);       // error.type tag, span status Error
    throw;                             // rethrown unchanged
}
measurement.Stop();
```

`Handle` is `async`, so anything thrown inside, including a DI failure while resolving a behavior, ends up in the
returned `Task` rather than being thrown from `Send` directly. Diagnostics are covered in section 9.

### 3.4 Which behaviors apply: `GetBehaviorTypes` and `Close`

The wrapper is static and shared by every provider in the process. The behaviors belong to one `IServiceCollection`:
two tests may build two applications with different behaviors. So the wrapper cannot keep "its" behavior list in a
plain field. It keeps a table keyed by the collection's `PipelineBehaviors` object:

```csharp
private readonly ConditionalWeakTable<PipelineBehaviors, ClosedBehaviors> closedBehaviors = new();

private Type[] GetBehaviorTypes(PipelineBehaviors behaviors)
{
    if (closedBehaviors.TryGetValue(behaviors, out ClosedBehaviors? cached) && cached.Version == behaviors.Version)
        return cached.Types;                                   // usual path: no work

    (Type[] types, int version) = behaviors.Close(typeof(TMessage), typeof(TResponse));
    closedBehaviors.AddOrUpdate(behaviors, new ClosedBehaviors(types, version));
    return types;
}
```

- `ConditionalWeakTable` does not keep its keys alive. When a test's collection is garbage collected, its entry
  disappears, and the static wrapper does not leak.
- `Version` invalidates the entry when a behavior was added after the first `Send` (possible in tests).

[`PipelineBehaviors.Close(messageType, responseType)`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Pipeline/PipelineBehaviors.cs) returns a named
tuple `(Type[] Types, int Version)`: the closed behavior types for this message, in pipeline order, and the version
they were computed for.

```csharp
Type serviceType = typeof(IPipelineBehavior<,>).MakeGenericType(messageType, responseType);
if (requestLogging)
    closedTypes.Add(typeof(RequestLoggingBehavior<,>).MakeGenericType(messageType, responseType));   // always first
foreach (Type behaviorType in behaviorTypes)                                                         // then as added
    if (BehaviorTypes.TryClose(behaviorType, serviceType, out Type? closedType))
        closedTypes.Add(closedType);                                                                 // only matching ones
```

So the pipeline order is: request logging, then the behaviors in the order of `AddBehavior` calls across `AddPheme`
calls. `AddFluentValidation` is just an `AddBehavior(typeof(ValidationBehavior<,>))`, so its position is where you
call it.

### 3.5 Closing open generic behaviors: `BehaviorTypes.TryClose`

[`BehaviorTypes`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Pipeline/BehaviorTypes.cs) answers: "does behavior type B apply to
`IPipelineBehavior<PlaceOrder, Guid>`, and if so, what is the closed type?"

- **Closed behavior** (`AuditPlaceOrder : IPipelineBehavior<PlaceOrder, Guid>`): it applies when `serviceType` is
  assignable from it.
- **Open generic behavior**: Pheme infers its type arguments from its `IPipelineBehavior<,>` interface, not from the
  position of its type parameters. Examples:

  | Behavior | Its interface | For `IPipelineBehavior<PlaceOrder, Guid>` |
  |---|---|---|
  | `Logging<TMessage, TResponse>` | `IPipelineBehavior<TMessage, TResponse>` | `TMessage=PlaceOrder`, `TResponse=Guid` |
  | `Timing<TCommand> where TCommand : ICommand` | `IPipelineBehavior<TCommand, Unit>` | `Unit` ≠ `Guid`: does not apply |
  | `Paging<TQuery, TItem>` | `IPipelineBehavior<TQuery, TItem[]>` | `Guid` is not an array: does not apply |

The algorithm, per `IPipelineBehavior<,>` interface of the behavior:

1. **`Bind`** walks the interface's type arguments (the pattern, which may contain the behavior's type parameters)
   next to the actual `PlaceOrder, Guid`, recursing into arrays and generic types. Each type parameter it meets
   records the actual type at its position. The same parameter met twice must bind to the same type.
2. Every type parameter must be bound. `AddBehavior` already rejected behaviors with a parameter that does not
   appear in the interface (`Validate` → `UsesAllTypeParameters`), so this holds for every behavior added.
3. **`SatisfiesConstraints`** checks `class`, `struct`, `new()` and type constraints by hand, substituting the bound
   arguments into constraints such as `where T : IEntity<T>`. The point is to return `false` without an exception:
   `MakeGenericType` would throw `ArgumentException` on every message the behavior does not apply to.
4. `MakeGenericType`, and a final `serviceType.IsAssignableFrom(candidate)` that also honours parts of the pattern
   without type parameters, which `Bind` skipped.

### 3.6 Building the onion: `HandlePipeline`

```csharp
Type[] types = GetBehaviorTypes(behaviors);          // e.g. [Logging, Timing, Validation]
if (types.Length == 0)
    return HandleMessage(message, serviceProvider, cancellationToken);

MessageHandlerDelegate<TResponse> next = token => HandleMessage(message, serviceProvider, token);   // the core
for (int index = types.Length - 1; index >= 0; index--)                                            // from the end
{
    var behavior = (IPipelineBehavior<TMessage, TResponse>)serviceProvider.GetRequiredService(types[index]);
    MessageHandlerDelegate<TResponse> inner = next;
    next = token => Invoke(behavior, message, inner, token);                                        // wrap
}
return next(cancellationToken);                                                                     // start outermost
```

The loop builds the onion from the inside out, so it walks the list backwards:

| After | `next` is |
|---|---|
| start | `handler` |
| index 2 | `Validation(next: handler)` |
| index 1 | `Timing(next: Validation(next: handler))` |
| index 0 | `Logging(next: Timing(next: Validation(next: handler)))` |

Calling `next` runs `Logging.Handle`. Its `await next(...)` runs `Timing.Handle`, and so on. Code after
`await next(...)` in a behavior is the "way back out". There are no separate calls on the way back:

```
Logging     start ─ await next ──────────────────────────────────────────── log "handled in 12.3 ms"
Timing                start ─ await next ─────────────────────── add header
Validation                        validate ─ await next ─── (nothing after)
Handler                                         Handle → Guid
```

Details that matter:

- **Behaviors are resolved before any runs**, from the scope's provider, by their *closed* type (`GetRequiredService(
  typeof(Timing<PlaceOrder, Guid>))`). Lifetimes therefore come from their own registrations: transient for
  `AddBehavior`, singleton for request logging.
- **The handler is resolved last**, inside `HandleMessage`. A behavior that does not call `next` (validation failed,
  cache hit) means the handler is never created.
- **`Invoke` turns a synchronous throw into a faulted task.** A non-`async` behavior that throws before returning a
  `Task` would otherwise throw out of the outer behavior's `next(...)` call itself, not out of its `await`. With
  `Invoke`, every `next` behaves the same way.
- `next` takes the `CancellationToken`, so a behavior may pass a different token inward (a timeout).

### 3.7 The core: `HandleMessage`

The three subclasses implement it. `CommandHandlerWrapper<TCommand>` is typical:

```csharp
ICommandHandler<TCommand> handler = GetHandler<ICommandHandler<TCommand>>(serviceProvider);
await handler.Handle(command, cancellationToken);
return default;      // Unit: commands without a response still flow through a pipeline of TResponse
```

`GetHandler` ([`MessageHandlerWrapper.cs`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Sending/MessageHandlerWrapper.cs)) uses `GetService` and
throws a readable `InvalidOperationException` when nothing is registered, naming the message by the `MessageKind` and
`MessageType` of its wrapper. The startup validation usually catches this earlier, but it checks only messages from
scanned assemblies, so a message defined elsewhere ends up here.

## 4. Request logging

[`RequestLoggingBehavior<TMessage, TResponse>`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Logging/RequestLoggingBehavior.cs) is an ordinary
behavior with three special properties:

- **Always first.** It is not in `behaviorTypes`; `Close` adds it in front (section 3.4).
- **Singleton per closed type.** It reads `IOptions<RequestLoggingOptions>.Value` once in its constructor. The logger
  category is `Pheme.Requests`.
- **It never changes the outcome.**

```csharp
try
{
    response = await next(cancellationToken);
}
catch (Exception exception) when (LogFailure(exception, startTimestamp, cancellationToken))
{
    throw;   // never reached: LogFailure returns false
}
```

`when (...)` is an **exception filter**. The runtime runs it *while looking for a handler*, before the stack unwinds.
`LogFailure` writes the log and returns `false`, so the `catch` block never runs and the exception continues to the
caller as if this `try` did not exist, stack trace untouched. If `LogFailure` itself throws (a broken `IsExpected`, a
failing logger provider), the runtime swallows that exception and treats the filter as `false`. The original
exception wins.

`LogFailure` chooses the log:

- cancelled by the caller (`OperationCanceledException` while the caller's token is cancelled), or
  `options.IsExpected(exception)` → `ExpectedFailure` at `ExpectedFailureLevel` (`Information`), message only;
- otherwise → `UnexpectedFailure` at `UnexpectedFailureLevel` (`Error`), with the exception.

The success log is written after the `try`, inside its own `try/catch` that ignores logging errors. The operation has
already completed and may have saved data, so logging must not turn it into a failure. The log calls themselves are
`[LoggerMessage]` source-generated methods in [`RequestLog`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Logging/RequestLog.cs).

## 5. Validation (`Pheme.FluentValidation`)

[`AddFluentValidation`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.FluentValidation/PhemeOptionsFluentValidationExtensions.cs) is built entirely
from the public extension points:

```csharp
FluentValidationSettings.GetOrAdd(options.Services)        // state in the collection, like section 1.2
    .SetExceptionFactory(exceptionFactory);                // only in the overload with a factory
options.AddBehavior(typeof(ValidationBehavior<,>))         // an ordinary behavior
       .AddAssemblyScanner(ValidatorScanner.Scan);         // finds validators next to handlers (section 1.4)
```

- [`ValidatorScanner.Scan`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.FluentValidation/ValidatorScanner.cs) registers every concrete closed
  `IValidator<T>` as transient with `TryAddEnumerable`, sorted by name, like handler scanning.
- [`FluentValidationSettings`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.FluentValidation/FluentValidationSettings.cs) holds one exception factory
  per collection. Modules may call `AddFluentValidation()` without a factory, and the composition root sets it once.
  A second, *different* factory throws.
- [`ValidationBehavior`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.FluentValidation/ValidationBehavior.cs) gets
  `IEnumerable<IValidator<TMessage>>` injected, runs the validators one after another (they may share a scoped
  `DbContext`), skips a validator type seen twice, collects all failures, and either throws the factory's exception
  or calls `next`.

Because `ValidationBehavior<TMessage, TResponse>` has the constraint `where TMessage : IMessage`, it applies to every
command and query. A message without validators gets an empty `IEnumerable` and passes straight to `next`.

## 6. Domain events: `IDomainEventPublisher`

[`DomainEventPublisher`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/DomainEvents/DomainEventPublisher.cs) works like `Sender` without behaviors:

```csharp
private static readonly ConcurrentDictionary<Type, IDomainEventHandlerWrapper> Wrappers = new();

private Task Dispatch(object domainEvent, CancellationToken cancellationToken)
{
    IDomainEventHandlerWrapper wrapper = Wrappers.GetOrAdd(
        domainEvent.GetType(),
        static eventType => (IDomainEventHandlerWrapper)Activator.CreateInstance(
            typeof(DomainEventHandlerWrapper<>).MakeGenericType(eventType))!);
    return wrapper.Handle(domainEvent, serviceProvider, instrumentation, cancellationToken);
}
```

The cache key is the **exact runtime type**. A handler of a base class does not receive a subclass event.

`Publish(IEnumerable<object>)` copies the list first, because handlers may add events to the entity the list came
from. Then it checks every element, so a `null` or a nested collection fails before any handler runs.

**The collection trap.** `List<OrderPlacedDomainEvent>` where `OrderPlacedDomainEvent` is a `struct` is not an
`IEnumerable<object>` (variance does not apply to value types), so C# picks `Publish(object)`. The whole list would be
dispatched as one event of type `List<OrderPlacedDomainEvent>`, which has no handlers, and nothing would happen.
`ThrowIfCollection` rejects any `IEnumerable` passed as a single event.

[`DomainEventHandlerWrapper<TEvent>.Handle`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/DomainEvents/DomainEventHandlerWrapper.cs):

1. starts one measurement for the event (`domain_event` kind), even with no handlers;
2. `serviceProvider.GetServices<IDomainEventHandler<TEvent>>()`, in registration order (by hand first, then scanned,
   by name);
3. awaits each handler in turn, skipping a handler *class* seen already (registered twice);
4. the first exception stops the rest and is rethrown, which rolls back the save the events came from.

## 7. EF Core (`Pheme.EntityFrameworkCore`)

### 7.1 Registration

[`AddPhemeDbContext<TContext>`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/PhemeDbContextServiceCollectionExtensions.cs):

```csharp
DbContextRegistration<TContext> registration = DbContextRegistration<TContext>.GetOrAdd(services);
configure(registration.Options);
registration.AddServices(services);
```

[`DbContextRegistration<TContext>`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/DbContextRegistration.cs) is the state of one
context, stored in the collection as a singleton instance like `HandlerRegistry`:

- **One `PhemeDbContextOptions` per context, configured by the call.** Every call for the context runs its delegate on
  the same instance, so calls add up, and contexts of different modules never see each other's settings. The
  interceptor, the writers and the store read the instance from DI. The options are applied at once, not lazily, because
  `UseOutbox<TModule>()` decides which services to register.
- **`AddServices` registers only what is new.** The interceptor, once, when the context has its first collector:

```csharp
services.TryAddScoped<DomainEventDispatchInterceptor<TContext>>();
services.AddSingleton<IValidateOptions<DomainEventDispatchValidationOptions<TContext>>>(
    new DomainEventDispatchValidation<TContext>(services));
services.AddOptions<DomainEventDispatchValidationOptions<TContext>>().ValidateOnStart();
services.ConfigureDbContext<TContext>((serviceProvider, options) =>
    options.AddInterceptors(DomainEventDispatchInterceptor<TContext>.Resolve(serviceProvider)));
```

  with the startup check of the context's options lifetime (below), and a scoped `IOutboxWriter<TModule>` →
  `EfOutboxWriter<TContext, TModule>` for each module not registered yet (the module list only grows, without
  duplicates, so a count of the registered ones is enough). With the first module the
  context adds its scoped `IOutboxStore` → `EfOutboxStore<TContext>` and registers itself once more, under the
  non-generic base class `DbContextRegistration`, for the startup check of all outboxes (section 7.3); the first
  context with an outbox in the collection adds that check.
- **`ConfigureDbContext<TContext>`** is EF Core's hook for adding to the options that `AddDbContext` builds. It does
  not matter whether `AddDbContext` comes before or after. EF runs the lambda when it builds `DbContextOptions`. With
  `AddDbContext` defaults that happens **inside the scope** the context is created in, so `serviceProvider` is the
  request scope. The interceptor, and the `IDomainEventPublisher` it depends on, come from the same scope as the
  `DbContext` the handlers use. This is also why pooling is not supported: pooled options are built once, outside any
  scope.
- **The startup check** [`DomainEventDispatchValidation<TContext>`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/DomainEventDispatchValidation.cs)
  holds the service collection and reads it on start, when it is final: the last registrations of
  `DbContextOptions<TContext>` and `IDbContextFactory<TContext>` must be scoped. Otherwise EF builds the options
  outside any scope - pooling, the default singleton `AddDbContextFactory`, a singleton `optionsLifetime`, and
  transient options whenever a singleton resolves the context or its factory - and the interceptor would come from
  the root provider. The marker options type is generic per context, so each context validates on its own, without
  option names.
- **`Resolve`** first reads the validated options of that check, since without a host nothing runs it on start, then
  checks that `IDomainEventPublisher` is registered. Options built outside a scope fail with the check's
  `OptionsValidationException`, and without `AddPheme` the error names the context, instead of the container's
  complaints about an internal type.

[`PhemeDbContextOptions.CollectDomainEventsFrom<TEntity>(get, clear)`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/PhemeDbContextOptions.cs)
stores a [`DomainEventCollector`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/DomainEventCollector.cs): the entity type plus the
two delegates, cast to work on `object`. This is how Pheme reads events without the domain referencing Pheme.

### 7.2 The interceptor

[`DomainEventDispatchInterceptor<TContext>`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/DomainEventDispatchInterceptor.cs) is a
`SaveChangesInterceptor`. EF Core calls `SavingChangesAsync` at the start of `SaveChangesAsync`, before anything is
written:

```csharp
for (int round = 0; ; round++)
{
    List<PendingEntity> pending = CollectPending(context);      // tracked entities that have events
    if (pending.Count == 0) return result;                      // nothing (left): let EF save
    if (round == maxRounds) throw ...;                          // handlers keep raising events: a cycle

    object[] domainEvents = [.. pending.SelectMany(entity => entity.DomainEvents)];
    foreach (PendingEntity entity in pending)
        entity.Collector.ClearDomainEvents(entity.Entity);      // clear BEFORE publishing
    await publisher.Publish(domainEvents, cancellationToken);   // section 6
}
```

Step by step for `PlaceOrder`:

1. The handler calls `Order.Place(...)`, which adds `OrderPlacedDomainEvent` to the entity's own list, then
   `db.Orders.Add` and `SaveChangesAsync`.
2. **Round 0.** `CollectPending` walks `ChangeTracker.Entries()`, finds the collector for each entity type
   (`FindCollector`, `IsAssignableFrom`, cached per type within the call), and copies its events. The events are
   cleared, then published.
3. `RecordOrderHistoryDomainEventHandler`, `WriteOrderPlacedToOutboxDomainEventHandler` and
   `AllocateStockDomainEventHandler` get the **same `DbContext`** (same scope) and add or modify entities, or an outbox
   message through `IOutboxWriter`. They do not call `SaveChanges`.
4. **Round 1.** If those handlers made entities raise new events, they are published now. Otherwise the loop returns.
5. The interceptor returns, and EF Core continues the same `SaveChangesAsync`. It saves everything tracked: the
   order and all changes of the handlers, in one transaction.

If any handler throws, the exception leaves `SavingChangesAsync`, EF Core never writes, and the exception reaches
the caller unchanged. The events were already cleared, so saving the same context again would not publish them at
all, and would store what the handlers changed before the exception: after an exception from a save the context - its
scope - is discarded, not saved again. EF Core's own retries (`EnableRetryOnFailure`) are safe: the execution strategy
repeats the writing inside the save, after this interceptor returned.

`FindCollector` throws when an entity matches two registered types (say, an interface and a base class): its events
would be published twice.

Before walking the entries, `CollectPending` checks that each collector matches an entity type of `context.Model`
(`IsAssignableFrom` against the CLR types of `GetEntityTypes()`), and throws naming the first collector that matches
none: a contract of another module would otherwise silently publish nothing. A passed check sets
`DbContextRegistration<TContext>.CollectorsMatchModel`, so later saves skip it; a failed one runs again on each save.

The synchronous `SavingChanges` cannot await async handlers without risking a deadlock. It throws only when there
are events to publish, so a plain `SaveChanges` with no events (seeding, migrations) still works.

**A save from inside the publication.** The loop runs between `publishing = context` and, in `finally`, restoring the
previous value (`publishing` is a field of the scoped interceptor). Both `SavingChanges` and `SavingChangesAsync` first
call `EnsureNotPublishing`, which throws when the context being saved is that same instance (`ReferenceEquals`): a
handler saving the publishing context would otherwise commit part of the change on its own. Another instance of
`TContext` in the scope shares the interceptor (the scoped options are one per scope) but is another reference, so
its save runs, publishes its own events and puts the previous value back.

### 7.3 The outbox table, writer and store

[`AddPhemeOutbox`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/PhemeModelBuilderExtensions.cs) maps `OutboxMessage` with
`ToTable("outbox_messages", schema: null)` - a `null` schema falls back to the model's default schema - a key on `Id`
with `ValueGeneratedNever`, and the index of the due messages' query. Everything else (column names, nullability from
the nullable annotations of `OutboxMessage`) comes from EF Core's conventions and the application's.

[`EfOutboxWriter<TContext, TModule>`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/EfOutboxWriter.cs) is created in the scope with
the scope's `TContext`:

```csharp
public void Add(IIntegrationEvent integrationEvent)
{
    DbContextRegistration<TContext>.OutboxMessages(context)       // checks the model and the database
        .Add(factory.Create(integrationEvent, registration.OutboxSource));
}
```

`OutboxMessages` throws when the model lacks `AddPhemeOutbox()` and then when
[`OutboxDatabases`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/OutboxDatabases.cs) does not list `Database.ProviderName` of the
context - a dictionary lookup per call, no cache: the same context type may use different databases in different
service providers (tests).

- **`IOutboxMessageFactory`** comes from `Pheme` (`AddOutbox`, section 8), resolved in the constructor after the
  startup check (below), so a missing `AddOutbox` without a host gives a readable error naming the context.
- **`OutboxSource`** is read on each `Add`: `PhemeDbContextOptions.OutboxSource`, or the context's type name without
  `DbContext`, computed once per registration.

[`EfOutboxStore<TContext>`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/EfOutboxStore.cs) is the context's `IOutboxStore`, created
by the outbox (section 8) in scopes of its own, so its `TContext` tracks nothing else:

- every query starts from the messages of its `Source` (taken once, in the constructor), through the same checks as
  `Add`; `TimeProvider`, registered by `AddOutbox`, is resolved in the constructor after the startup check, like the
  writer's factory;
- `GetPending` and `GetParked`: unprocessed, `AttemptCount` below or from `maxAttempts` - `GetPending` also with
  `NextAttemptOnUtc` null or not after `now` (converted to UTC) -, `OrderBy(OccurredOnUtc).ThenBy(Id)`, `Take`,
  `AsNoTracking` - the runner changes the instances it gets; `GetByType` (for `Pheme.Testing`, section 8.5): every
  message of the type, `OrderBy(OccurredOnUtc).ThenBy(Id)`, `AsNoTracking`;
- `MarkAsProcessed`, `MarkAsFailed`, `Requeue`, `Redeliver`: one `ExecuteUpdate` each (`MarkAsFailed` sets
  `AttemptCount + 1` in SQL, stores `NextAttemptOnUtc` in UTC and reads the count back; `Requeue` and `Redeliver`
  clear `NextAttemptOnUtc`), `DeleteProcessedBefore` one `ExecuteDelete`; none goes through `SaveChanges`, so the
  context's interceptors do not run;
- `GetStats`: two counts and a `Min` over the unprocessed messages.

**Startup check:** [`OutboxValidation`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.EntityFrameworkCore/OutboxValidation.cs), one
`IValidateOptions<OutboxValidationOptions>` with `ValidateOnStart`, gets every `DbContextRegistration` with an outbox
and reports a missing `IOutboxMessageFactory` per context (asking `IServiceProviderIsService`, without creating it), a
module in two contexts and a source in two contexts. The writer and the store read `IOptions<OutboxValidationOptions>`
in their constructors, so without a host the first resolution fails with the same errors. The model is not checked at
start: that would create the context.

## 8. Outbox delivery

`AddOutbox` ([`PhemeOptions`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/PhemeOptions.cs)) registers singletons: the `IOutboxMessageFactory` the
writers use, `IntegrationEventTypes`, `IntegrationEventDispatcher`, `OutboxRunner` as `IOutboxRunner`,
`TimeProvider.System` unless one is registered, and `OutboxProcessor` as an `IHostedService`; and the scoped
`OutboxMaintenance` as `IOutboxMaintenance`. The registration of the
dispatcher is also how the startup validation (section 2) knows the outbox is added.

```
OutboxProcessor (BackgroundService)          only loops and waits
  └─ IOutboxRunner.RunOnce                   one run at a time (SemaphoreSlim)
       ├─ short scope: GetPending(now) of every IOutboxStore
       └─ for each message:
            ├─ IntegrationEventDispatcher.Dispatch              span + duration around it
            │    └─ IntegrationEventHandlerWrapper<TEvent>       deserialize; a scope to read the handler classes,
            │                                                    then a new scope per handler not completed yet
            └─ new scope: MarkAsProcessed / MarkAsFailed(next attempt) of the message's store
```

### 8.1 From a stored name to typed handlers

[`IntegrationEventTypes`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/IntegrationEventTypes.cs) maps stored names to event types. It is built
on first use from `HandlerRegistry.Services`, the complete collection, so handlers registered by hand after `AddPheme`
count: every closed, non-keyed `IIntegrationEventHandler<TEvent>` registration contributes its `TEvent`, named by
[`IntegrationEventNames`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/IntegrationEventNames.cs) - the `[IntegrationEventName]`, otherwise
`FullName`, the same rule the message factory writes with. Types the startup validation rejects (abstract, generic,
without a valid name) are left out; of a duplicate name, the first type wins. The message factory
([`OutboxMessageFactory`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/OutboxMessageFactory.cs)) reads it too: it throws for an event whose
stored name does not map back to the event's own runtime type, so an event without a handler never reaches the table.

[`IntegrationEventDispatcher.Dispatch`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/IntegrationEventDispatcher.cs) looks the type up and takes a
wrapper from a static cache, one per event type, like `DomainEventPublisher`. The wrapper
([`IntegrationEventHandlerWrapper<TEvent>`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/IntegrationEventHandlerWrapper.cs)) deserializes the
content with `OutboxSerialization.Options`, then reads the handler classes in a scope of its own (resolving
`IIntegrationEventHandler<TEvent>` and taking the distinct `GetType()`s, in registration order: a registration by hand
may be a factory). Each class gets a new scope from the dispatcher's `IServiceScopeFactory`, in which the handlers are
resolved again and the one of that class is called, one after another. A handler whose name (`TypeNames.FormatStored` of
its class, a nested class with `+`) is among the completed ones is skipped without a scope; one that completes is added;
one that fails is collected and the others still run. The dispatcher never throws for a failed delivery: an unknown
type, content that does not deserialize, a handler that cannot be resolved and the handlers' failures (an
`AggregateException`) all come back in `IntegrationEventDispatchResult.Error`, with the completed names.

### 8.2 The runner

[`OutboxRunner.RunOnce`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/OutboxRunner.cs) waits for its semaphore, so a test calling it while
another run is in progress waits instead of delivering the same messages. Then:

1. A short scope resolves every `IOutboxStore` and reads a batch (`BatchSize`, `MaxAttempts`, and `now` of the
   `TimeProvider`, one for all stores) from each.
2. For each message, `Deliver` starts the delivery's span and measurement
   (`MessageInstrumentation.StartIntegrationEvent`) and dispatches; the wrapper creates the scopes (section 8.1). A
   single failure is unwrapped from the `AggregateException`.
3. If the delivery failed after the token was cancelled, it throws `OperationCanceledException` without recording
   anything.
4. A **new scope** resolves the stores again and picks the one with the message's `Source`: `MarkAsProcessed`, or
   `MarkAsFailed` with `Exception.ToString()`, the completed handlers joined by line breaks (`HandlerSeparator`;
   the next attempt splits them before the dispatch) and the next attempt: `now + OutboxOptions.GetRetryDelay(attempt)`
   (`RetryDelay` doubled per earlier failure, at most `MaxRetryDelay`), or `null` when this attempt, the
   `MaxAttempts`-th by the count read with the message, parks it. Both with `CancellationToken.None`. A failed attempt
   is logged ([`OutboxProcessorLog`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/OutboxProcessorLog.cs)): a warning with the delay, or an error
   once the returned `AttemptCount` reaches `MaxAttempts`.

The result counts the processed messages, lists the failed ones (the instances read from the store, updated with what
`MarkAsFailed` recorded) and says whether a store returned a full batch (`HasMore`).

### 8.3 The processor

[`OutboxProcessor`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/OutboxProcessor.cs) returns at once when `ProcessInBackground` is off.
Otherwise it calls `RunOnce` in a loop and, unless the run reported `HasMore` (without failures when `RetryDelay` is
zero: a failed message would then be at the head of the next batch), waits `PollingInterval` with
`Task.Delay(interval, timeProvider, stoppingToken)`. A run that throws is logged and treated as
"wait"; cancellation of `stoppingToken` ends the loop.

Before a run, once `RetentionInterval` (1 hour) has passed since the last one - the first time on start - it deletes
the processed messages older than `ProcessedRetention`: a new scope, `DeleteProcessedBefore` on each store. Each store
that succeeds is logged (`RetentionCompleted`, at `RetentionLogLevel`) with the number of deleted messages and the time
of the next deletion, which is known before the deletion starts; a failed store is logged (`RetentionFailed`) and skipped.

### 8.4 Maintenance, health check and gauges

All three read the stores through the methods that only they use:

- [`OutboxMaintenance`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/OutboxMaintenance.cs) (scoped) gets the stores of its scope. `GetParked`
  asks every store for `limit` messages and keeps the oldest `limit` (by `OccurredOnUtc`, then `Id`); `Requeue` asks
  every store; `RequeueAllParked` pages through `GetParked` (100 at a time) and requeues each message until a page is
  not full.
- [`OutboxHealthCheck`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/OutboxHealthCheck.cs) is created by the health check service for each
  run, in the run's scope (the factory in
  [`PhemeHealthChecksBuilderExtensions`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/PhemeHealthChecksBuilderExtensions.cs)), and turns
  `GetStats` of every store into the status and the data.
- [`OutboxMetrics.Observe`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Outbox/OutboxMetrics.cs), called by the `OutboxRunner` constructor, creates the
  two observable gauges on the provider's meter (`MessageInstrumentation.Meter`). Their callbacks open a scope, call
  `GetStats` of every store synchronously and skip a store that throws.

### 8.5 Test helpers (`Pheme.Testing`)

[`PhemeTestingServiceProviderExtensions`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.Testing/PhemeTestingServiceProviderExtensions.cs) uses only the
public API of `Pheme`. It repeats the stored format of `IntegrationEventNames` and `OutboxSerialization.Options` - the
`[IntegrationEventName]` or the full type name, JSON with the web defaults - so it finds and reads events as the
writer stores them.

- `UsePhemeOutboxForTests` `PostConfigure`s `ProcessInBackground = false` and `RetryDelay = TimeSpan.Zero` (a failed
  message is due again in the next drain) and registers the singleton
  [`OutboxDrainState`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.Testing/OutboxDrainState.cs): the identifiers of the failed messages already
  reported for this provider.
- `DrainOutbox` calls `IOutboxRunner.RunOnce` until a run processes nothing or `maxRounds` runs are done. After each
  run, the failed messages not in `OutboxDrainState` are added to it and thrown in one `InvalidOperationException`.
- `GetOutboxEvents` opens a scope, calls `GetByType` with the stored name of `TEvent` on the selected stores, orders the
  messages of all of them by `OccurredOnUtc`, then `Id`, and deserializes the content; `Redeliver` does the same on all
  stores and calls `Redeliver` of the store for each processed message the predicate accepts, removing it from
  `OutboxDrainState`.

[`PhemeLayerRule`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme.Testing/PhemeLayerRule.cs) is reflection only, independent of the outbox helpers.
`Check(assembly)` lists the `Pheme.*` names of `GetReferencedAssemblies()` its rule does not allow, then walks the
signatures of all types of the assembly for the forbidden types (the store contracts); `Check(assembly, namespace)`
walks the types of the namespace for both forbidden assemblies and forbidden types. The walk takes each type's base
types, interfaces and declared members - fields without `[CompilerGenerated]`, properties, events, constructors and
methods without property and event accessors - and breaks every member type into its generic arguments and element types. The violations
are deduplicated, sorted and thrown in one `InvalidOperationException`.

## 9. Diagnostics

[`MessageInstrumentation`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Diagnostics/MessageInstrumentation.cs) is a singleton per provider:

- the `ActivitySource` named `Pheme` is **static**, because the BCL has no factory for it;
- the `Meter` comes from `IMeterFactory` when one is registered (ASP.NET Core does that), so metrics of separate
  providers (tests) do not mix. Without a factory, the instance creates its own meter and disposes it with the
  provider. The container disposes `IDisposable` singletons it created;
- the histogram `pheme.message.duration` is in seconds, with ASP.NET Core's bucket boundaries.

`Start(messageType, messageKind)` calls `ActivitySource.StartActivity(messageType)`. That returns `null` when nobody
listens, which is how tracing costs nothing without OpenTelemetry. The span becomes `Activity.Current`, so database
spans of the handler become its children.

[`MessageMeasurement`](https://github.com/dawid1103/Pheme/blob/master/src/Pheme/Diagnostics/MessageMeasurement.cs) is a `readonly struct`, so there is no
allocation per message. `Stop(exception)`:

1. records the duration if the histogram has a listener (`duration.Enabled`), with tags `pheme.message.type`,
   `pheme.message.kind` and, on failure, `error.type`;
2. on failure sets `error.type` and status `Error` on the span;
3. disposes the span (which ends it).

The exception type for `error.type` is formatted only when the histogram or the span listens, so a failure without
a listener costs nothing extra. Each step is in a `try/catch` that ignores listener exceptions. Telemetry must not
fail a completed operation or replace the exception it failed with.

The same instrumentation is used by `MessageHandlerWrapper` (around the whole pipeline), by
`DomainEventHandlerWrapper` (around all handlers of one event) and by `OutboxRunner` (around the dispatch of one
message). `StartIntegrationEvent` starts the activity with `ActivityKind.Consumer` and, when the message has a valid
`TraceParent`, an `ActivityLink` to it, parsed only when the source has listeners; it adds the tag
`pheme.outbox.source`. The outbox gauges live on the same meter (section 8.4).

## 10. All the caches in one table

| Cache | Where | Key | Lives | Filled |
|---|---|---|---|---|
| command wrappers | `Sender.CommandWrappers` | message type | process | first `Send` of the type |
| command/query wrappers with response | `Sender.ResponseWrappers<TResponse>` | message type | process | first `Send` of the type |
| domain event wrappers | `DomainEventPublisher.Wrappers` | event type | process | first `Publish` of the type |
| integration event wrappers | `IntegrationEventDispatcher.Wrappers` | event type | process | first delivery of the type |
| handler names | `IntegrationEventHandlerWrapper<TEvent>.HandlerNames` | handler type | process | first delivery to the handler |
| stored event names | `IntegrationEventNames.Names` | event type | process | first write or startup map of the type |
| stored name → event type | `IntegrationEventTypes` (singleton) | stored name | provider | creation of `IntegrationEventDispatcher` |
| closed behavior types | each wrapper's `ConditionalWeakTable` | `PipelineBehaviors` (= collection) + `Version` | as long as the collection | first `Send` per collection, again after a change |
| type names and kinds | `static readonly` fields of generic classes | closed generic type | process | first use of the type |
| registration validation | `IOptionsMonitor<HandlerRegistrationOptions>` | provider | provider | host start or first `ISender` |
| outbox contexts validation | `IOptions<OutboxValidationOptions>` | provider | provider | host start or first EF writer or store |
| options lifetime validation | `IOptions<DomainEventDispatchValidationOptions<TContext>>` | provider + context type | provider | host start or first options of the context |
| collectors match the model | `DbContextRegistration<TContext>.CollectorsMatchModel` | context type | as long as the collection | first save of the context that passes the check |

## 11. One request, end to end

`POST /orders` in the example application, with request logging, `CommandTimingBehavior` and FluentValidation:

1. `OrdersController` gets the scoped `ISender` → `Sender` constructor → validation result from the options cache.
2. `Send(PlaceOrderCommand)` → `ResponseWrappers<Guid>.Commands` → `CommandHandlerWrapper<PlaceOrderCommand, Guid>`.
3. `Handle` → span `Ordering.Application.Orders.PlaceOrder.PlaceOrderCommand` starts.
4. `GetBehaviorTypes` → cached `[RequestLogging<,>, CommandTiming<,>, Validation<,>]`, all closed for
   `PlaceOrderCommand, Guid`.
5. The onion is built. `RequestLogging` starts its stopwatch, `CommandTiming` its own, and `Validation` runs
   `PlaceOrderCommandValidator`, which passes.
6. `HandleMessage` resolves `PlaceOrderCommandHandler` (transient) with the scoped `DbContext`.
7. The handler calls `SaveChangesAsync`. The interceptor publishes `OrderPlacedDomainEvent` →
   `DomainEventHandlerWrapper<OrderPlacedDomainEvent>` → its own `domain_event` span →
   `RecordOrderHistoryDomainEventHandler`, `WriteOrderPlacedToOutboxDomainEventHandler`, then
   `AllocateStockDomainEventHandler` (registered by hand before `AddPheme` in `AddOrderingApplication`, so they keep
   this order instead of the scan's alphabetical one).
8. EF Core saves the order, the history, the outbox message and the stock in one transaction.
9. Way back out: the handler returns the id, `CommandTiming` adds `Server-Timing`, `RequestLogging` logs
   `command ... handled in ... ms`, and the span ends with the duration recorded.
10. The controller returns `201 Created`.

If validation had failed in step 5, `ValidationBehavior` would have thrown before step 6, so no handler and no
`DbContext` work. `RequestLogging` would log an expected failure (the example marks `ValidationException` as
expected), the span would get `error.type`, and the exception would reach the controller unchanged.
