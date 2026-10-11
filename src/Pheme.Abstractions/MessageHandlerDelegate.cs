using System.Diagnostics.CodeAnalysis;

namespace Pheme
{
    /// <summary>
    /// Continues the pipeline: invokes the next behavior, or the handler after the last behavior.
    /// </summary>
    /// <typeparam name="TResponse">The response type, <see cref="Unit"/> for an <see cref="ICommand"/>.</typeparam>
    /// <param name="cancellationToken">
    /// The token passed to the rest of the pipeline and the handler; usually the one the behavior received.
    /// </param>
    /// <returns>
    /// The response of the handler, possibly replaced by an inner behavior. A failure of the rest of the pipeline is
    /// returned as a faulted task, never thrown synchronously.
    /// </returns>
    /// <remarks>
    /// May be called again, for example by a behavior that retries: each call runs the same instances of the inner
    /// behaviors and resolves the handler anew, a new instance of a transient handler, from the same scope - so a
    /// scoped <c>DbContext</c> still holds what the failed call left in it.
    /// </remarks>
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "Named like ASP.NET Core's RequestDelegate: the delegate a middleware-style step calls to continue.")]
    public delegate Task<TResponse> MessageHandlerDelegate<TResponse>(CancellationToken cancellationToken);
}
