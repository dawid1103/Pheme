# Request logging

`AddRequestLogging` logs how the handling of every command and query ended and how long it took, one log per
message, in the category `Pheme.Requests`. It is off until called: without it Pheme writes no logs. It is always the
outermost behavior, whatever the order of `AddBehavior` calls, so the time includes validation and the application's
behaviors. The exception always reaches the caller unchanged. Domain events are not logged; they have
[spans and metrics](12-tracing-and-metrics.md).

| Outcome | Level (default) | Message |
|---|---|---|
| handled | `SuccessLevel` (`Debug`) | `command Shop.PlaceOrder handled in 12.3 ms` |
| expected exception: `IsExpected`, or cancellation by the caller | `ExpectedFailureLevel` (`Information`) | `... failed in 3.1 ms with Shop.OrderClosedException: Order is closed.` |
| any other exception | `UnexpectedFailureLevel` (`Error`) | `... failed in 3.1 ms with System.InvalidOperationException`, with the exception |

**Without configuration** every exception is unexpected, so a failed validation is an `Error` with a stack trace.
Most applications therefore set `IsExpected` (see the examples below).

## Options

`AddRequestLogging(logging => ...)` configures `RequestLoggingOptions`:

| Option | Type | Default | Meaning |
|---|---|---|---|
| `IsExpected` | `Func<Exception, bool>` | expects none | Exceptions that are an outcome rather than an error, such as validation and domain exceptions: logged on `ExpectedFailureLevel`, with their message and without the stack trace. Cancellation by the caller is always expected. |
| `SuccessLevel` | `LogLevel` | `Debug` | Level of the log of a handled message. |
| `ExpectedFailureLevel` | `LogLevel` | `Information` | Level of the log of an expected exception. |
| `UnexpectedFailureLevel` | `LogLevel` | `Error` | Level of the log of any other exception, logged with the exception. |

`LogLevel.None` turns the log of that outcome off; an undefined level throws `ArgumentOutOfRangeException`. A
predicate that throws is ignored: that failure is not logged and the original exception still reaches the caller.

| Property | Logs | Value |
|---|---|---|
| `MessageKind` | all | `command` or `query` |
| `MessageType` | all | full type name of the message |
| `ElapsedMs` | all | time of the whole pipeline and handler, in milliseconds |
| `ExceptionType` | failures | full type name of the exception |
| `ExceptionMessage` | expected failures | the exception's message |

Event ids: 1 `MessageHandled`, 2 `MessageExpectedFailure`, 3 `MessageFailed`.

## Examples

Defaults: success on `Debug`, every exception on `Error`:

```csharp
options.AddRequestLogging();
```

Validation and domain exceptions as an outcome, not an error. The types are the application's own
([`Program.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Program.cs)):

```csharp
options.AddRequestLogging(logging =>
    logging.IsExpected = exception => exception is ValidationException or NotFoundException or BusinessRuleException);
```

An exception handler or filter logs errors already, so Pheme does not log them twice; success is logged on
`Information`, above the default minimum level of ASP.NET Core:

```csharp
options.AddRequestLogging(logging =>
{
    logging.IsExpected = exception => exception is ValidationException or NotFoundException;
    logging.SuccessLevel = LogLevel.Information;
    logging.UnexpectedFailureLevel = LogLevel.None;
});
```

Several calls, for example one per module, configure the same options in call order, and `IsExpected` set later
replaces the earlier one. A module adds its own exceptions to the predicate set before it:

```csharp
options.AddRequestLogging(logging =>
{
    Func<Exception, bool> previous = logging.IsExpected;
    logging.IsExpected = exception => previous(exception) || exception is OrderClosedException;
});
```

The options can also be configured outside `AddPheme`, for example in a test host:

```csharp
services.Configure<RequestLoggingOptions>(logging => logging.SuccessLevel = LogLevel.Information);
```

`Debug` is below the default minimum level of ASP.NET Core: turn success logs on in `appsettings.json` with the
category (`RequestLoggingOptions.CategoryName`), or set `SuccessLevel`:

```json
{
  "Logging": {
    "LogLevel": {
      "Pheme.Requests": "Debug"
    }
  }
}
```

Request logging needs an `ILoggerFactory` in the container; every host registers one, and without it the first
`Send` fails with the container's error. Durations without logs, as metrics, come from
[tracing and metrics](12-tracing-and-metrics.md), which need no `AddRequestLogging`.

---

Previous: [Validation](05-validation.md) · [Contents](../README.md) · Next: [Domain events](07-domain-events.md)
