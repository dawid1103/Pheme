using System.Diagnostics.CodeAnalysis;

namespace Pheme
{
    /// <summary>
    /// Sends commands and queries to their handlers, through the pipeline behaviors.
    /// </summary>
    /// <remarks>
    /// Each message goes to exactly one handler, resolved from the dependency injection scope this sender was resolved
    /// from, and runs in-process on the caller's call path. Exceptions thrown by the handler or a behavior reach the
    /// caller unchanged, not wrapped.
    /// </remarks>
    [SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple public overloads with optional parameters",
        Justification = "An optional CancellationToken is the .NET convention; interface members cannot be added later without a breaking change anyway.")]
    public interface ISender
    {
        /// <summary>
        /// Sends a command that returns no response.
        /// </summary>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">A token passed to the behaviors and the handler.</param>
        /// <returns>A task that completes when the command has been handled.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
        Task Send(ICommand command, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a command that returns a response.
        /// </summary>
        /// <typeparam name="TResponse">The type of the response.</typeparam>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">A token passed to the behaviors and the handler.</param>
        /// <returns>The response returned by the handler.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
        Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a query.
        /// </summary>
        /// <typeparam name="TResponse">The type of the response.</typeparam>
        /// <param name="query">The query to send.</param>
        /// <param name="cancellationToken">A token passed to the behaviors and the handler.</param>
        /// <returns>The response returned by the handler.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
        Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
    }
}
