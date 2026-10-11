using System.Diagnostics.CodeAnalysis;

namespace Pheme
{
    /// <summary>
    /// Wraps the handling of messages, for cross-cutting concerns such as logging, validation or transactions.
    /// </summary>
    /// <typeparam name="TMessage">The type of the wrapped message.</typeparam>
    /// <typeparam name="TResponse">
    /// The response type of the message; <see cref="Unit"/> for an <see cref="ICommand"/>, which has no response.
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// Added with <c>PhemeOptions.AddBehavior</c>, usually as an open generic class applied to every message that
    /// satisfies its generic constraints, such as <c>where TMessage : ICommand</c> for commands without a response.
    /// Registering it as a service of this interface has no effect.
    /// </para>
    /// <para>
    /// Behaviors run in the order they were added, the first one outermost. Each send resolves them from the caller's
    /// dependency injection scope.
    /// </para>
    /// </remarks>
    public interface IPipelineBehavior<in TMessage, TResponse> where TMessage : IMessage
    {
        /// <summary>
        /// Handles the message, usually by calling <paramref name="next"/> and returning its response.
        /// </summary>
        /// <param name="message">The message being sent.</param>
        /// <param name="next">
        /// Continues the pipeline. Not calling it skips the inner behaviors and the handler; the response returned
        /// here goes to the caller.
        /// </param>
        /// <param name="cancellationToken">The token passed by the caller of <see cref="ISender"/>, or by an outer behavior.</param>
        /// <returns>The response returned to the outer behavior, or to the caller.</returns>
        [SuppressMessage(
            "Naming",
            "CA1716:Identifiers should not match keywords",
            Justification = "'next' is the idiom of ASP.NET Core middleware; 'Next' is a keyword only in Visual Basic.")]
        Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
    }
}
