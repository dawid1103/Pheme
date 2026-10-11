using Pheme;

namespace Shop.Api.Behaviors
{
    /// <summary>
    /// Times the commands with a response; see <see cref="CommandTiming"/>.
    /// </summary>
    internal sealed class CommandTimingBehavior<TCommand, TResponse>(IHttpContextAccessor httpContextAccessor)
        : IPipelineBehavior<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public Task<TResponse> Handle(TCommand message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
            CommandTiming.Measure(httpContextAccessor, typeof(TCommand), next, cancellationToken);
    }
}
