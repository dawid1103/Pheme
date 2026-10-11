namespace Pheme
{
    /// <summary>
    /// Handles a query.
    /// </summary>
    /// <typeparam name="TQuery">The type of the handled query.</typeparam>
    /// <typeparam name="TResponse">The type of the response.</typeparam>
    /// <remarks>
    /// One handler per message type, resolved from the caller's dependency injection scope; may be
    /// <see langword="internal"/>. One class may handle several messages.
    /// </remarks>
    public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
    {
        /// <summary>
        /// Handles the query.
        /// </summary>
        /// <param name="query">The query to handle.</param>
        /// <param name="cancellationToken">The token passed by the caller of <see cref="ISender"/>.</param>
        /// <returns>The response returned to the caller.</returns>
        Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken);
    }
}
