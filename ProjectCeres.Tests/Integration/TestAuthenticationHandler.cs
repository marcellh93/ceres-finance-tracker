using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Auto-authenticates every WAF test request as the sentinel user.
/// Lets pre-existing CRUD integration tests continue to work without per-test
/// login boilerplate. Stage 6a auth tests register their own scheme overrides.
/// </summary>
public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sid = Guid.NewGuid();
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, new Guid("00000000-0000-0000-0000-000000000001").ToString()),
                new Claim(ClaimTypes.Name, "test@local"),
                new Claim(SessionConstants.SessionIdClaim, sid.ToString()),
            ],
            SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
