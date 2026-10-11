using Pheme;
using Shop.Common.Errors;
using Shop.Common.Security;

namespace Shop.Api.Behaviors
{
    /// <summary>
    /// Lets a message that requires a role (<see cref="IRequireRole"/>) through only when the current user is in that
    /// role; otherwise throws <see cref="ForbiddenException"/>, answered with 403, before the message is validated or
    /// handled. The rule belongs to the message, so it holds for every caller, not only for one endpoint.
    /// </summary>
    internal sealed class AuthorizationBehavior<TMessage, TResponse>(ICurrentUser user) : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage, IRequireRole
    {
        public Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (!user.IsInRole(TMessage.RequiredRole))
            {
                throw new ForbiddenException($"Only the role '{TMessage.RequiredRole}' may send {typeof(TMessage).Name}.");
            }

            return next(cancellationToken);
        }
    }
}
