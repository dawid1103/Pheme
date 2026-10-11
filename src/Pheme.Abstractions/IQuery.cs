namespace Pheme
{
    /// <summary>
    /// A query that reads state and returns a response.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
    /// <remarks>
    /// Handled by exactly one <see cref="IQueryHandler{TQuery, TResponse}"/>. A query should not change state;
    /// Pheme does not enforce this, but behaviors may rely on it.
    /// </remarks>
    public interface IQuery<TResponse> : IMessage;
}
