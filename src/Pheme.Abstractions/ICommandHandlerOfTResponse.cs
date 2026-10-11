namespace Pheme
{
    /// <summary>
    /// Handles a command that returns a response.
    /// </summary>
    /// <typeparam name="TCommand">The type of the handled command.</typeparam>
    /// <typeparam name="TResponse">The type of the response.</typeparam>
    /// <remarks>
    /// One handler per message type, resolved from the caller's dependency injection scope; may be
    /// <see langword="internal"/>. One class may handle several messages.
    /// </remarks>
    public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
    {
        /// <summary>
        /// Handles the command.
        /// </summary>
        /// <param name="command">The command to handle.</param>
        /// <param name="cancellationToken">The token passed by the caller of <see cref="ISender"/>.</param>
        /// <returns>The response returned to the caller.</returns>
        Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken);
    }
}
