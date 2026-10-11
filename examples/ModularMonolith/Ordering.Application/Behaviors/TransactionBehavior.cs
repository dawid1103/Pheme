using Microsoft.EntityFrameworkCore.Storage;
using Pheme;

namespace Ordering.Application.Behaviors
{
    /// <summary>
    /// Handles an <see cref="ITransactionalCommand"/> in a transaction of the ordering module's <c>DbContext</c>, committed
    /// when the handler completes and rolled back when anything throws. The handler's saves, the domain event handlers
    /// they run and its direct writes all happen inside it.
    /// </summary>
    /// <remarks>
    /// Commands with a response would need a second class, as <c>CommandTimingBehavior</c> shows; the module has none.
    /// With a retrying execution strategy (<c>EnableRetryOnFailure</c>) EF Core refuses a transaction begun this way:
    /// the whole command would have to run through <c>Database.CreateExecutionStrategy()</c>, in a new scope per attempt.
    /// </remarks>
    public sealed class TransactionBehavior<TCommand>(IOrderingDbContext db) : IPipelineBehavior<TCommand, Unit>
        where TCommand : ITransactionalCommand
    {
        /// <inheritdoc />
        public async Task<Unit> Handle(TCommand message, MessageHandlerDelegate<Unit> next, CancellationToken cancellationToken)
        {
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            Unit response = await next(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
    }
}
