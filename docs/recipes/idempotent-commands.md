# Idempotent commands

A client that lost the response retries; with an idempotency key the retry answers the first response instead of
placing a second order. A behavior saves the key and the response in one transaction with the handler's changes
([`IdempotencyBehavior.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Ordering.Application/Behaviors/IdempotencyBehavior.cs)):

```csharp
public interface IIdempotentCommand<TResponse> : ICommand<TResponse>
{
    Guid? IdempotencyKey { get; }
}

public sealed class IdempotencyBehavior<TCommand, TResponse>(IOrderingDbContext db, TimeProvider timeProvider)
    : IPipelineBehavior<TCommand, TResponse>
    where TCommand : IIdempotentCommand<TResponse>
{
    public async Task<TResponse> Handle(TCommand message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (message.IdempotencyKey is not Guid key)
        {
            return await next(cancellationToken);
        }

        ProcessedCommand? processed = await db.ProcessedCommands.AsNoTracking()
            .SingleOrDefaultAsync(p => p.IdempotencyKey == key, cancellationToken);
        if (processed is not null)
        {
            return JsonSerializer.Deserialize<TResponse>(processed.Response)!;
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        TResponse response = await next(cancellationToken);
        db.ProcessedCommands.Add(ProcessedCommand.Record(key, JsonSerializer.Serialize(response), timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
```

The key comes from the `Idempotency-Key` header
([`OrdersController.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Controllers/OrdersController.cs)),
kept out of the JSON body with `[JsonIgnore]`:

```csharp
public sealed record PlaceOrderCommand(string Product, int Quantity, bool Express = false) : IIdempotentCommand<Guid>, IAuditedCommand
{
    [JsonIgnore]
    public Guid? IdempotencyKey { get; init; }
}

Guid id = await sender.Send(command with { IdempotencyKey = idempotencyKey }, cancellationToken);
```

Two requests with one key at once both get past the check, but the key is the table's primary key: the later save
fails, its transaction rolls the order back, and it answers 409 like any conflict. The key is not compared with the
content of the command, and a command that is also transactional would begin a second transaction, which EF Core
refuses: such a behavior would join `Database.CurrentTransaction` instead.

---

Previous: [Caching queries](caching-queries.md) · [Contents](../README.md) · Next: [Audit trail](audit-trail.md)
