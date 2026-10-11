# Audit trail

An audit entry - who, when, what - saved with the change it records: the behavior adds it to the module's `DbContext`
before the handler, and the handler's `SaveChangesAsync` writes both in one transaction. A command that fails, or
saves nothing, leaves no entry
([`AuditBehavior.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Ordering.Application/Behaviors/AuditBehavior.cs)):

```csharp
public interface IAuditedCommand : IMessage;

public sealed class AuditBehavior<TCommand, TResponse>(IOrderingDbContext db, ICurrentUser user, TimeProvider timeProvider)
    : IPipelineBehavior<TCommand, TResponse>
    where TCommand : IAuditedCommand
{
    public Task<TResponse> Handle(TCommand message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        db.AuditTrail.Add(AuditEntry.Record(user.Name, typeof(TCommand).Name, JsonSerializer.Serialize(message), timeProvider.GetUtcNow()));
        return next(cancellationToken);
    }
}

options.AddBehavior(typeof(IdempotencyBehavior<,>));
options.AddBehavior(typeof(AuditBehavior<,>));   // inside idempotency: a repeated command is not audited again
options.AddBehavior(typeof(TransactionBehavior<>));
```

The marker belongs to the module, as the entry goes to the module's context: a behavior for every command would add
entries to the ordering context for the commands of the shipping module, whose handlers never save it. An audit of
attempts, failed ones included, is a log or a save of its own after `next`, in another transaction.

---

Previous: [Idempotent commands](idempotent-commands.md) · [Contents](../README.md)
