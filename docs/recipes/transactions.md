# Transactions

A command's `SaveChangesAsync` saves its tracked changes and those of its domain event handlers in one transaction, so
most commands need no other. `ExecuteUpdate`, `ExecuteDelete` and SQL write at once, outside that save, and EF Core
opens the save's transaction only after the handlers read what they need ([Publishing on save](../guide/07-domain-events.md#publishing-on-save),
"Also"). A command that writes directly next to its tracked changes, or whose reads need isolation, runs in a
transaction of its own, opened by a behavior for the commands that ask for it with a marker of their module
([`TransactionBehavior.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Ordering.Application/Behaviors/TransactionBehavior.cs)):

```csharp
public interface ITransactionalCommand : ICommand;

public sealed class TransactionBehavior<TCommand>(IOrderingDbContext db) : IPipelineBehavior<TCommand, Unit>
    where TCommand : ITransactionalCommand
{
    public async Task<Unit> Handle(TCommand message, MessageHandlerDelegate<Unit> next, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Unit response = await next(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}

options.AddFluentValidation(...);
options.AddBehavior(typeof(TransactionBehavior<>));   // innermost: around the handler only
```

In the example, receiving a delivery raises the stock and closes the product's restock requests with one
`ExecuteDelete` ([`ReceiveStock`](https://github.com/dawid1103/Pheme/tree/master/examples/ModularMonolith/Ordering.Application/Inventory/ReceiveStock)):

```csharp
public sealed record ReceiveStockCommand(string Product, int Quantity) : ITransactionalCommand, IRequireRole { ... }

// ReceiveStockCommandHandler
await db.RestockRequests.Where(r => r.Product == command.Product).ExecuteDeleteAsync(cancellationToken);
stock.Receive(command.Quantity);
await db.SaveChangesAsync(cancellationToken);   // publishes the domain events inside the transaction too
```

When the save fails - an order placed in between changed the stock, so the request answers 409 - the transaction rolls
the delete back; without it the requests would be gone while the stock stayed as it was.

- **One module, one marker.** The behavior opens the transaction of its module's `DbContext`, so the marker belongs to
  that module; another module with its own context has its own marker and behavior.
- **A command with a response** needs a second class, `IPipelineBehavior<TCommand, TResponse>` with
  `where TCommand : ICommand<TResponse>`, as the timing behavior in [Pipeline behaviors](../guide/04-pipeline-behaviors.md)
  shows.
- **Retrying execution strategies.** With `EnableRetryOnFailure` EF Core refuses a transaction the application begins:
  run the whole command through `Database.CreateExecutionStrategy()` instead, in a new scope per attempt, so a retry
  does not see what the failed attempt left in the context.

---

Previous: [Pipeline behaviors](../guide/04-pipeline-behaviors.md) · [Contents](../README.md) · Next: [Authorization](authorization.md)
