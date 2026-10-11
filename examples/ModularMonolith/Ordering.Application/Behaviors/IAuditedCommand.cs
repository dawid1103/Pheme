using Pheme;

namespace Ordering.Application.Behaviors
{
    /// <summary>
    /// A command of the ordering module recorded in its audit trail by <see cref="AuditBehavior{TCommand, TResponse}"/>:
    /// who sent it, when, and its content, saved with the change it made.
    /// </summary>
    public interface IAuditedCommand : IMessage;
}
