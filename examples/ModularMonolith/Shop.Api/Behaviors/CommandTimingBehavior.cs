using Pheme;

namespace Shop.Api.Behaviors
{
    /// <summary>
    /// Times the commands without a response; see <see cref="CommandTiming"/>.
    /// </summary>
    internal sealed class CommandTimingBehavior<TCommand>(IHttpContextAccessor httpContextAccessor)
        : IPipelineBehavior<TCommand, Unit>
        where TCommand : ICommand
    {
        public Task<Unit> Handle(TCommand message, MessageHandlerDelegate<Unit> next, CancellationToken cancellationToken) =>
            CommandTiming.Measure(httpContextAccessor, typeof(TCommand), next, cancellationToken);
    }
}
