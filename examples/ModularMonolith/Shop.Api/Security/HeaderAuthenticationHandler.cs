using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Shop.Api.Security
{
    /// <summary>
    /// Authenticates the user named by the <c>X-User</c> header, in the roles of the comma-separated <c>X-Roles</c>
    /// header. A stand-in that keeps the example free of an identity provider: it trusts whatever the client sends, so a
    /// real application authenticates with JWT bearer tokens or cookies instead and keeps the rest unchanged.
    /// </summary>
    internal sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Header";
        public const string UserHeader = "X-User";
        public const string RolesHeader = "X-Roles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? name = Request.Headers[UserHeader];
            if (string.IsNullOrEmpty(name))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            IEnumerable<Claim> roles = Request.Headers[RolesHeader]
                .SelectMany(value => (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Select(role => new Claim(ClaimTypes.Role, role));
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, name), .. roles], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
