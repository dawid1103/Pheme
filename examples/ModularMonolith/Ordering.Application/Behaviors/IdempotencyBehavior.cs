using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pheme;

namespace Ordering.Application.Behaviors
{
    /// <summary>
    /// Handles an <see cref="IIdempotentCommand{TResponse}"/> once per idempotency key: the key and the response are saved
    /// in one transaction with the handler's changes, and the command sent again with that key answers the saved
    /// response without being handled.
    /// </summary>
    /// <remarks>
    /// Two requests with one key at once are both handled, but only the first to commit saves: the other fails on the
    /// key's primary key, rolls its changes back and answers 409. The key is not checked against the content of the
    /// command; a command that is also an <see cref="ITransactionalCommand"/> would begin a second transaction, which EF Core
    /// refuses.
    /// </remarks>
    public sealed class IdempotencyBehavior<TCommand, TResponse>(IOrderingDbContext db, TimeProvider timeProvider)
        : IPipelineBehavior<TCommand, TResponse>
        where TCommand : IIdempotentCommand<TResponse>
    {
        /// <inheritdoc />
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
}
