namespace Pheme
{
    /// <summary>
    /// A command that changes state and returns a response, for example the identifier of a created entity.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
    /// <remarks>
    /// Handled by exactly one <see cref="ICommandHandler{TCommand, TResponse}"/>. Does not derive from
    /// <see cref="ICommand"/>, so a behavior constrained to <see cref="ICommand"/> does not apply to it.
    /// </remarks>
    public interface ICommand<TResponse> : IMessage;
}
