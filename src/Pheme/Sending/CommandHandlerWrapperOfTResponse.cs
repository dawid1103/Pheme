namespace Pheme.Sending
{
    internal sealed class CommandHandlerWrapper<TCommand, TResponse> : MessageHandlerWrapper<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        protected override async Task<TResponse> HandleMessage(TCommand command, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ICommandHandler<TCommand, TResponse> handler = GetHandler<ICommandHandler<TCommand, TResponse>>(serviceProvider);
            return await handler.Handle(command, cancellationToken);
        }
    }
}
