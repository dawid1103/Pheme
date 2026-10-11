namespace Pheme
{
    /// <summary>
    /// Common base of every message sent through <see cref="ISender"/>, for constraining pipeline behaviors to all
    /// messages.
    /// </summary>
    /// <remarks>
    /// Do not implement it directly: a message type implements exactly one of <see cref="ICommand"/>,
    /// <see cref="ICommand{TResponse}"/> and <see cref="IQuery{TResponse}"/>. The registration validation reports
    /// any other message type.
    /// </remarks>
    public interface IMessage;
}
