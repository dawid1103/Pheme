using System.Text.Json;
using Pheme;
using Shop.Common.Security;

namespace Ordering.Application.Behaviors
{
    /// <summary>
    /// Records an <see cref="IAuditedCommand"/> in the audit trail of the ordering module. The entry is added to the
    /// module's <c>DbContext</c> before the handler runs and saved by the handler's <c>SaveChangesAsync</c>, in one
    /// transaction with the change: a command that fails, or saves nothing, leaves no entry.
    /// </summary>
    public sealed class AuditBehavior<TCommand, TResponse>(IOrderingDbContext db, ICurrentUser user, TimeProvider timeProvider)
        : IPipelineBehavior<TCommand, TResponse>
        where TCommand : IAuditedCommand
    {
        /// <inheritdoc />
        public Task<TResponse> Handle(TCommand message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            db.AuditTrail.Add(AuditEntry.Record(user.Name, typeof(TCommand).Name, JsonSerializer.Serialize(message), timeProvider.GetUtcNow()));
            return next(cancellationToken);
        }
    }
}
