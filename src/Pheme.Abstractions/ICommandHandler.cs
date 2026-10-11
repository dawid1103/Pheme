namespace Pheme
{
    /// <summary>
    /// Handles a command that returns no response.
    /// </summary>
    /// <typeparam name="TCommand">The type of the handled command.</typeparam>
    /// <remarks>
    /// One handler per message type, resolved from the caller's dependency injection scope; may be
    /// <see langword="internal"/>. One class may handle several messages.
    /// </remarks>
    public interface ICommandHandler<in TCommand> where TCommand : ICommand
    {
        /// <summary>
        /// Handles the command.
        /// </summary>
        /// <param name="command">The command to handle.</param>
        /// <param name="cancellationToken">The token passed by the caller of <see cref="ISender"/>.</param>
        /// <returns>A task that completes when the command has been handled.</returns>
        Task Handle(TCommand command, CancellationToken cancellationToken);
    }
}
