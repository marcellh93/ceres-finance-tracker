using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ProjectCeres.Tests.Integration.Authentication;

public class ArchitectureTests
{
    private static readonly Assembly App = typeof(global::Program).Assembly;

    [Fact]
    public void No_api_controller_class_has_AllowAnonymous()
    {
        // Legacy Razor controllers under Controllers/* (not Controllers/Api/*) are
        // permitted class-level AllowAnonymous because they are SPA-shell or 302-redirect
        // holdovers slated for deletion in Batch 4. New API controllers must declare
        // anonymity per-method to make the choice explicit and reviewable.
        var violations = App.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.Namespace?.Contains(".Api") == true)
            .Where(t => t.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(t => t.FullName!)
            .ToList();

        violations.Should().BeEmpty(
            "API controllers must declare AllowAnonymous at the method level only — class-level lets new actions inherit anonymity by accident");
    }

    [Fact]
    public void Every_controller_action_declares_authorization_intent()
    {
        var violations = App.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
            .Where(m => m.GetCustomAttribute<AuthorizeAttribute>() is null
                     && m.GetCustomAttribute<AllowAnonymousAttribute>() is null
                     && m.DeclaringType!.GetCustomAttribute<AuthorizeAttribute>() is null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        violations.Should().BeEmpty(
            "explicit [Authorize] or [AllowAnonymous] required on every action — global fallback hides the intent");
    }

    [Fact]
    public void Api_HttpGet_actions_must_not_have_write_verb_names()
    {
        // Legacy Razor controllers under Controllers/* are 302-redirect holdovers
        // whose action names mirror legacy URL paths (e.g. Edit, Create, Confirm,
        // Dispute) but the action body is a Redirect — not a state change. They are
        // slated for deletion in Batch 4. The API surface is where naming hygiene
        // actually maps to behaviour.
        var forbiddenPrefixes = new[]
        {
            "Create", "Update", "Delete", "Remove",
            "Archive", "Deactivate", "Disable", "Enable", "Restore",
            "Reset", "Submit", "Send", "Process",
            "Approve", "Reject", "Confirm", "Dispute", "Cancel",
            "Upload", "Set"
        };

        var violations = App.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => t.Namespace?.Contains(".Api") == true)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<HttpGetAttribute>() is not null)
            .Where(m => forbiddenPrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        violations.Should().BeEmpty("API GET endpoints must be side-effect-free per RFC 9110");
    }

    [Fact]
    public void FailedLoginAttempt_NotInGlobalQueryFilterList()
    {
        // ADR-0065 § Decision-1 enumerates the user-owned entities that receive
        // HasQueryFilter. FailedLoginAttempt is intentionally NOT in that list.
        // The test enforces the intent: when Stage 7 wires global filters,
        // FailedLoginAttempt must remain unfiltered (per ADR-0067 § Decision-6 —
        // failed-login retention purge is a cross-tenant operation).
        //
        // For now (pre-Stage-7), assert the entity has UserId nullable so the
        // schema permits attempts against unknown users. The real "no global
        // filter" assertion lands when Stage 7 wires global filters.
        var entityType = typeof(ProjectCeres.Models.FailedLoginAttempt);
        var userIdProp = entityType.GetProperty("UserId");
        userIdProp.Should().NotBeNull();
        userIdProp!.PropertyType.Should().Be(typeof(Guid?));
    }

    [Fact]
    public void FailedLoginAttempt_HasRequiredIndexes()
    {
        var factory = new ProjectCeres.Tests.Integration.AuthTestWebApplicationFactory();
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProjectCeres.Data.AppDbContext>();
            var entity = db.Model.FindEntityType(typeof(ProjectCeres.Models.FailedLoginAttempt));
            entity.Should().NotBeNull();
            var indexes = entity!.GetIndexes()
                .Select(i => i.Properties.Select(p => p.Name).ToArray())
                .ToList();
            indexes.Should().Contain(props => props.SequenceEqual(new[] { "IpAddress", "OccurredAt" }));
            indexes.Should().Contain(props => props.SequenceEqual(new[] { "EmailAttempted", "OccurredAt" }));
            indexes.Should().Contain(props => props.SequenceEqual(new[] { "OccurredAt" }));
        }
        finally
        {
            factory.Dispose();
        }
    }

    [Fact]
    public void AuthController_NoCallsToTwoFactorAuthenticatorSignInAsync()
    {
        // The 6b.1 latent-bug source. 6b.2 replaced this with VerifyTwoFactorTokenAsync.
        // Catch any regression that re-introduces the framework helper that mutates
        // password lockout state on TOTP failures.
        var path = ResolveControllerSourcePath("AuthController.cs");
        var source = System.IO.File.ReadAllText(path);
        // Match a call site (trailing parenthesis), not comment text that documents the replacement.
        source.Should().NotContain("TwoFactorAuthenticatorSignInAsync(",
            "use VerifyTwoFactorTokenAsync + manual SignInAsync to keep the password lockout counter clean");
    }

    [Fact]
    public void AuthController_AllErrorReturnsUseEnvelopeShape()
    {
        // No flat-string error returns. Every Unauthorized() in AuthController must
        // route through UnauthorizedEnvelope(code, message) per api-contract.md.
        var path = ResolveControllerSourcePath("AuthController.cs");
        var source = System.IO.File.ReadAllText(path);
        source.Should().NotContain("Unauthorized(new { error = \"",
            "use UnauthorizedEnvelope() with { code, message } per api-contract.md");
        source.Should().NotContain("error = \"locked_out\"");
        source.Should().NotContain("error = \"replay\"");
    }

    [Fact]
    public void LoginTotp_OnlyAcceptsPost()
    {
        // AuthController.LoginTotp must have [HttpPost("login/totp")] and no [HttpGet].
        // A GET on a login endpoint would allow CSRF via link navigation — the method
        // must be POST-only so the antiforgery token requirement actually has teeth.

        var authControllerType = App.GetTypes()
            .Single(t => t.Name == "AuthController" && typeof(ControllerBase).IsAssignableFrom(t));

        // Confirm no method on AuthController has [HttpGet] with route ending in "login/totp"
        var httpGetOnLoginTotp = authControllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<HttpGetAttribute>() is { } attr
                        && (attr.Template?.EndsWith("login/totp", StringComparison.OrdinalIgnoreCase) == true
                            || m.Name.Equals("LoginTotp", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        httpGetOnLoginTotp.Should().BeEmpty(
            "LoginTotp must not have [HttpGet] — GET requests bypass antiforgery and are replay-prone");

        // Confirm the LoginTotp method exists with [HttpPost("login/totp")]
        var loginTotpMethod = authControllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SingleOrDefault(m => m.Name == "LoginTotp");

        loginTotpMethod.Should().NotBeNull("AuthController must have a LoginTotp method");

        var httpPost = loginTotpMethod!.GetCustomAttribute<HttpPostAttribute>();
        httpPost.Should().NotBeNull("LoginTotp must be decorated with [HttpPost]");
        httpPost!.Template.Should().Be("login/totp",
            "LoginTotp route must match login/totp");
    }

    [Fact]
    public void MfaController_AllErrorReturnsUseEnvelopeShape()
    {
        // MfaController must use the { error: { code, message } } envelope shape for
        // all error returns — no flat-string patterns like BadRequest(new { error = "..." }).
        // Gap 4 + Gap 5 fixed the new branches; this test catches regressions and also
        // validates that the OLD branches (EnrollVerify lines 58 + 65) still use flat strings.
        // If those old branches are found, STOP and report — they need migration.
        var path = ResolveControllerSourcePath("MfaController.cs");
        var source = System.IO.File.ReadAllText(path);

        // Flat-string patterns: BadRequest(new { error = "<string>" })
        source.Should().NotContain("BadRequest(new { error = \"",
            "MfaController must use envelope shape { error: { code, message } } — " +
            "found flat-string BadRequest return (EnrollVerify has stale branches that need migration)");

        // Also guard against Unauthorized with flat strings (belt-and-suspenders)
        source.Should().NotContain("Unauthorized(new { error = \"",
            "MfaController must use envelope shape for Unauthorized returns too");
    }

    /// <summary>
    /// Resolves a controller source path by walking up from the test bin/ output to
    /// the repository root, then descending into the production project. xUnit runs
    /// from bin/Debug/net10.0/ so we walk up four levels to reach repo root.
    /// </summary>
    private static string ResolveControllerSourcePath(string fileName)
    {
        var baseDir = AppContext.BaseDirectory; // .../ProjectCeres.Tests/bin/Debug/net10.0/
        var repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", ".."));
        var resolved = System.IO.Path.Combine(repoRoot, "ProjectCeres", "Controllers", "Api", fileName);
        if (!System.IO.File.Exists(resolved))
            throw new System.IO.FileNotFoundException($"Could not resolve controller source: {resolved}");
        return resolved;
    }
}
