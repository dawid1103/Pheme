namespace Pheme.Sending
{
    internal sealed class QueryHandlerWrapper<TQuery, TResponse> : MessageHandlerWrapper<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        protected override async Task<TResponse> HandleMessage(TQuery query, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            IQueryHandler<TQuery, TResponse> handler = GetHandler<IQueryHandler<TQuery, TResponse>>(serviceProvider);
            return await handler.Handle(query, cancellationToken);
        }
    }
}
