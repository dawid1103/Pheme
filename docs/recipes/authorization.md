# Authorization

ASP.NET Core authorizes endpoints; a behavior authorizes messages, whichever endpoint, job or handler sends them. The
message names its role in its type, as a static abstract member, so a command cannot leave it out and the behavior
reads it without reflection
([`IRequireRole.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Common/Security/IRequireRole.cs),
[`AuthorizationBehavior.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Behaviors/AuthorizationBehavior.cs)):

```csharp
public interface IRequireRole
{
    static abstract string RequiredRole { get; }
}

public sealed record DispatchShipmentCommand(Guid OrderId) : ICommand, IRequireRole
{
    public static string RequiredRole => Roles.Warehouse;
}

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

options.AddRequestLogging(...);
options.AddBehavior(typeof(AuthorizationBehavior<,>));   // first: a refused message is neither validated nor handled
```

One class covers commands and queries, with a response or without; messages without the interface skip it. The
exception is the application's, answered with 403 by its exception filter. `ICurrentUser` is the application's too:
the API implements it from `HttpContext.User`
([`HttpCurrentUser.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Security/HttpCurrentUser.cs)), so outside a request - a hosted service, the
handler of an integration event - there is no user, and a message requiring a role is refused. The example
authenticates with request headers, a stand-in for JWT bearer tokens or cookies that trusts the client
([`HeaderAuthenticationHandler.cs`](https://github.com/dawid1103/Pheme/blob/master/examples/ModularMonolith/Shop.Api/Security/HeaderAuthenticationHandler.cs)); the behavior does not change
with the scheme.

---

Previous: [Transactions](transactions.md) · [Contents](../README.md) · Next: [Caching queries](caching-queries.md)
