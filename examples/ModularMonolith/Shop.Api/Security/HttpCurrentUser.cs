using Shop.Common.Security;

namespace Shop.Api.Security
{
    /// <summary>
    /// The user of the current HTTP request, as authenticated by the request's scheme; none outside a request.
    /// </summary>
    internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
    {
        public string? Name => httpContextAccessor.HttpContext?.User.Identity?.Name;

        public bool IsInRole(string role) => httpContextAccessor.HttpContext?.User.IsInRole(role) ?? false;
    }
}
