# Configuration

Pheme does not read `IConfiguration` itself: every option is a property of an options class, set in code. To keep a
value in `appsettings.json` or an environment variable, bind a configuration section to the options class; Pheme has
no section names of its own, the application picks them.

## Options

Everything that can be bound from configuration, with the value used when it is not set. Each class is set in the
callback named in its heading.

### `OutboxOptions` - `AddOutbox(outbox => ...)`

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `PollingInterval` | `TimeSpan` | 5 seconds | How long the processor waits before it looks for new messages again; must be above zero. |
| `BatchSize` | `int` | 20 | How many due messages it reads from each store at a time; at least 1. |
| `MaxAttempts` | `int` | 10 | After how many failed attempts a message is parked; at least 1. |
| `RetryDelay` | `TimeSpan` | 10 seconds | The wait after the first failed attempt, doubled after each next one; not negative. |
| `MaxRetryDelay` | `TimeSpan` | 15 minutes | The longest wait between attempts; above zero. |
| `ProcessInBackground` | `bool` | `true` | Whether the hosted service delivers messages; `false` leaves it to `IOutboxRunner.RunOnce`. |
| `ProcessedRetention` | `TimeSpan?` | 30 days | How long processed messages are kept; empty is `null`: forever. Above zero otherwise. |
| `RetentionLogLevel` | `LogLevel` | `Information` | The level of the log written for each store when processed messages are deleted; `None` logs nothing. |

### `RequestLoggingOptions` - `AddRequestLogging(logging => ...)`

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SuccessLevel` | `LogLevel` | `Debug` | The level of the log of a handled message; `None` logs nothing. |
| `ExpectedFailureLevel` | `LogLevel` | `Information` | The level when handling fails with an expected exception. |
| `UnexpectedFailureLevel` | `LogLevel` | `Error` | The level when handling fails with any other exception. |

### `PhemeDbContextOptions` - `AddPhemeDbContext<TContext>(..., options => ...)`

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `MaxDomainEventRounds` | `int` | 10 | How many rounds of domain event publication one save may run; at least 1. |
| `OutboxSource` | `string?` | the context's type name without `DbContext` | The name of the context's outbox, stored with its messages; not white space. |

What each property does in full is in the XML docs and in the chapter of its feature:
[request logging](06-request-logging.md), [the outbox](08-integration-events.md) and
[operating it](09-operating-the-outbox.md), [domain events](07-domain-events.md).

### Only in code

These are methods, types and delegates, not values, so configuration cannot set them: `RequestLoggingOptions.IsExpected`;
`AddHandlersFromAssemblies`, `AddBehavior` and `AddAssemblyScanner` of `AddPheme`; `CollectDomainEventsFrom` and
`UseOutbox` of `AddPhemeDbContext`; the parameters of `AddPhemeOutboxCheck` (`maxPendingAge`, 5 minutes by default, and `tags`) and the
exception factory of `AddFluentValidation`.

## From `appsettings.json`

Bind the section inside the callback; the example application does it for the outbox
([`Program.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Program.cs)):

```csharp
builder.Services.AddPheme(options =>
{
    options.AddOutbox(outbox => builder.Configuration.GetSection("Outbox").Bind(outbox));
    options.AddRequestLogging(logging =>
    {
        builder.Configuration.GetSection("RequestLogging").Bind(logging);
        logging.IsExpected = exception => exception is ValidationException; // a delegate: always in code
    });
});
```

```json
{
  "Outbox": {
    "PollingInterval": "00:00:02",
    "MaxAttempts": 5,
    "ProcessedRetention": "7.00:00:00"
  },
  "RequestLogging": {
    "SuccessLevel": "Information"
  }
}
```

The same keys work as environment variables, `Outbox__PollingInterval=00:00:02`. The options may also be bound
outside `AddPheme`, with `services.Configure<OutboxOptions>(configuration.GetSection("Outbox"))` or the same for
`RequestLoggingOptions`; the calls configure one instance in call order.

- **Format.** A `TimeSpan` is `"hh:mm:ss"` or `"d.hh:mm:ss"`, a level is the name of a `LogLevel`
  (`"Information"`, `"None"`). An empty `ProcessedRetention` is `null`: processed messages are kept forever.
- **Keys left out keep their defaults**, and so do the properties that are not bound, such as `IsExpected`.
- **A wrong value fails when the section is bound**: in `AddPhemeDbContext` at once, in the other options when they
  are first used, which is the start of the application for the outbox and the first message for request logging. A
  value that is not a `TimeSpan` or a level throws an `InvalidOperationException` naming the key; a value the property rejects,
  such as `BatchSize` of `0`, throws the property's `ArgumentOutOfRangeException` inside a
  `TargetInvocationException`. A misspelled key is ignored, as everywhere in configuration.
- **Changes need a restart.** Pheme reads the options once; it does not follow `IOptionsMonitor`.

Log levels of the categories are not Pheme's options but the standard `Logging:LogLevel` of the host:
`Pheme.Requests` ([`RequestLoggingOptions.CategoryName`](06-request-logging.md)) and `Pheme.Outbox`
(`OutboxOptions.CategoryName`).

---

Previous: [Tracing and metrics](12-tracing-and-metrics.md) · [Contents](../README.md) · Next: [Limitations](14-limitations.md)
