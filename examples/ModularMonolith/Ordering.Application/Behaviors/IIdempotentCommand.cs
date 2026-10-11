using Pheme;

namespace Ordering.Application.Behaviors
{
    /// <summary>
    /// A command of the ordering module that a client may send again safely: with the same
    /// <see cref="IdempotencyKey"/>, <see cref="IdempotencyBehavior{TCommand, TResponse}"/> answers the response of the
    /// first time instead of handling it again.
    /// </summary>
    /// <typeparam name="TResponse">The response, stored as JSON with the key.</typeparam>
    public interface IIdempotentCommand<TResponse> : ICommand<TResponse>
    {
        /// <summary>
        /// The key the client chose for this command, the same when it sends the command again; without a key the
        /// command is handled every time.
        /// </summary>
        Guid? IdempotencyKey { get; }
    }
}
