namespace Pheme
{
    /// <summary>
    /// A command that changes state and returns no response.
    /// </summary>
    /// <remarks>
    /// Handled by exactly one <see cref="ICommandHandler{TCommand}"/>. Inside the pipeline its response type is
    /// <see cref="Unit"/>; callers of <see cref="ISender"/> never see it.
    /// </remarks>
    public interface ICommand : IMessage;
}
