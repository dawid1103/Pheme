namespace Pheme.Sending
{
    internal sealed class CommandHandlerWrapper<TCommand> : MessageHandlerWrapper<TCommand, Unit>
        where TCommand : ICommand
    {
        protected override async Task<Unit> HandleMessage(TCommand command, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ICommandHandler<TCommand> handler = GetHandler<ICommandHandler<TCommand>>(serviceProvider);
            await handler.Handle(command, cancellationToken);
            return default;
        }
    }
}
