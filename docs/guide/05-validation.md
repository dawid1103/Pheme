# Validation

`AddFluentValidation` (`Pheme.FluentValidation`) registers the `IValidator<T>` classes of every assembly added with
`AddHandlersFromAssemblies`, `internal` ones too, and runs the validators of a message one after another before its
handler. The failures of all validators are thrown together: as `FluentValidation.ValidationException` by default,
or as the application's own exception, made by the factory passed to `AddFluentValidation` (see
[Getting started](02-getting-started.md)).

```csharp
options.AddFluentValidation(failures => new ValidationException(failures
    .GroupBy(failure => failure.PropertyName)
    .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray())));
```

The example's `ValidationException` is its own, which its exception filter answers with 400 and the errors per field
([`Program.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Program.cs),
[`ApiExceptionFilter.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Errors/ApiExceptionFilter.cs)).

- **A behavior like the others:** it validates where `AddFluentValidation` is called among the `AddBehavior` calls,
  so behaviors added before it, such as authorization, run before the validation.
- **Any failure stops the message**, a failure of severity `Warning` too, and the handler is not called.
- **One at a time.** Validators run one after another, never in parallel, because they often share a scoped
  `DbContext`; each gets a context of its own, so the failures of one are not repeated by the next.
- **Transient**, like the handlers; a validator registered twice runs once.
- **Every message** passes through the behavior; one without validators goes straight on.
- **Not logged as expected by itself:** request logging cannot know the application's exception type, so add it to
  `IsExpected` ([Request logging](06-request-logging.md)).

---

Previous: [Pipeline behaviors](04-pipeline-behaviors.md) · [Contents](../README.md) · Next: [Request logging](06-request-logging.md)
