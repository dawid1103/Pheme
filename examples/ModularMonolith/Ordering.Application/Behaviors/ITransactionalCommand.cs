using Pheme;

namespace Ordering.Application.Behaviors
{
    /// <summary>
    /// A command of the ordering module handled in a database transaction of its own, opened by
    /// <see cref="TransactionBehavior{TCommand}"/>: for a handler that writes with <c>ExecuteUpdate</c>,
    /// <c>ExecuteDelete</c> or SQL next to its tracked changes, or whose reads need isolation. A command that only
    /// changes tracked entities needs none: its <c>SaveChangesAsync</c> saves them in one transaction.
    /// </summary>
    public interface ITransactionalCommand : ICommand;
}
