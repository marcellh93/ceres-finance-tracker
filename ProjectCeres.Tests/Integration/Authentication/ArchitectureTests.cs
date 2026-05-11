using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;

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

    // -----------------------------------------------------------------------
    // #40 — PasswordResetController action attributes
    // -----------------------------------------------------------------------

    [Fact]
    public void PasswordResetController_methods_have_correct_attributes()
    {
        // The action method is named RequestReset (not Request) to avoid collision with
        // ControllerBase.Request property. Confirm and RequestReset must both carry
        // [AllowAnonymous] and [EnableRateLimiting].
        var type = typeof(ProjectCeres.Controllers.Api.PasswordResetController);
        foreach (var methodName in new[] { "RequestReset", "Confirm" })
        {
            var mi = type.GetMethod(methodName);
            mi.Should().NotBeNull($"PasswordResetController must define {methodName}");
            mi!.GetCustomAttributes(true)
                .Any(a => a is AllowAnonymousAttribute)
                .Should().BeTrue($"{methodName} must be [AllowAnonymous]");
            mi.GetCustomAttributes(true)
                .Any(a => a is EnableRateLimitingAttribute)
                .Should().BeTrue($"{methodName} must be [EnableRateLimiting]");
        }
    }

    // -----------------------------------------------------------------------
    // #41 — PasswordResetController does not hold a PasswordHasher field
    // -----------------------------------------------------------------------

    [Fact]
    public void PasswordResetController_does_not_call_PasswordHasher_directly()
    {
        // The controller should depend only on PasswordResetService, never on
        // Argon2idPasswordHasher directly — hashing is the service's responsibility.
        var type = typeof(ProjectCeres.Controllers.Api.PasswordResetController);
        var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        fields.Select(f => f.FieldType)
            .Should().NotContain(typeof(ProjectCeres.Common.Authentication.Argon2idPasswordHasher),
                "the controller must delegate password hashing to PasswordResetService");
    }

    // -----------------------------------------------------------------------
    // #42 — Only known IEmailService implementations exist in the production assembly
    // -----------------------------------------------------------------------

    [Fact]
    public void IEmailService_dev_impl_is_LogOnly()
    {
        // LogOnlyEmailService is the sole dev/production implementation.
        // NoopEmailService ships in the production assembly (test-helper role)
        // but must never be registered in production DI.
        // This test guards against accidental addition of a third implementation
        // (e.g. a real SMTP sender) before Stage 8 intentionally wires one.
        var asm = typeof(IEmailService).Assembly;
        var impls = asm.GetTypes()
            .Where(t => typeof(IEmailService).IsAssignableFrom(t)
                     && !t.IsAbstract
                     && !t.IsInterface)
            .ToList();

        var knownImpls = new[]
        {
            typeof(LogOnlyEmailService),
            typeof(NoopEmailService),
        };

        impls.Should().OnlyContain(t => knownImpls.Contains(t),
            "only LogOnlyEmailService and NoopEmailService may exist in the production assembly — " +
            "any new implementation (real SMTP, etc.) must be gated by Stage 8");

        impls.Should().Contain(typeof(LogOnlyEmailService),
            "LogOnlyEmailService must be present as the dev IEmailService implementation");
    }

    // -----------------------------------------------------------------------
    // #43 — PasswordResetService public methods accept CancellationToken
    // -----------------------------------------------------------------------

    [Fact]
    public void PasswordResetService_methods_take_CancellationToken()
    {
        var type = typeof(ProjectCeres.Common.Authentication.PasswordResetService);
        var publicMethods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "RequestAsync" or "ConfirmAsync")
            .ToList();

        publicMethods.Should().NotBeEmpty("PasswordResetService must expose RequestAsync and ConfirmAsync");

        foreach (var m in publicMethods)
        {
            var lastParam = m.GetParameters().LastOrDefault();
            lastParam.Should().NotBeNull($"{m.Name} must have at least one parameter");
            lastParam!.ParameterType.Should().Be(typeof(CancellationToken),
                $"{m.Name} must end with CancellationToken so callers can propagate cancellation");
        }
    }

    // -----------------------------------------------------------------------
    // #44 — PasswordResetController actions do not log PII (email / password)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PasswordResetController_actions_dont_log_request_body()
    {
        // Full integration assertion: replay a request and a confirm, then verify
        // captured logs contain neither the literal email nor the literal new password.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var capturedLogs = new List<string>();

        // WithCapturedLogger returns WebApplicationFactory<Program>; chain the email
        // replacement via a second WithWebHostBuilder to keep the base AuthTestWebApplicationFactory
        // pipeline (real Identity, real CSRF, real rate-limit).
        await using var factory = new AuthTestWebApplicationFactory()
            .WithCapturedLogger(capturedLogs)
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IEmailService>();
                    services.AddSingleton<IEmailService>(new NoopEmailService());
                }));

        var client = factory.CreateClient();

        var email = $"arch-pii-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, email);
        var sentinelPassword = $"sentinel-{Guid.NewGuid():N}-passw0rd";

        await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });

        // The confirm token will be invalid, but we only care about log content — not the outcome.
        await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = "irrelevant-token", newPassword = sentinelPassword });

        capturedLogs.Should().NotContain(s => s.Contains(email),
            "the request body email must not appear in any log line");
        capturedLogs.Should().NotContain(s => s.Contains(sentinelPassword),
            "the new password must not appear in any log line");
    }

    // -----------------------------------------------------------------------
    // #27 — RequireRecentAuth attribute is only on action methods, not classes
    // -----------------------------------------------------------------------

    [Fact]
    public void RequireRecentAuth_attribute_is_only_on_action_methods_not_classes()
    {
        var asm = typeof(ProjectCeres.Common.Authentication.RequireRecentAuthAttribute).Assembly;
        var controllerTypes = asm.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var t in controllerTypes)
        {
            t.GetCustomAttributes(typeof(ProjectCeres.Common.Authentication.RequireRecentAuthAttribute), inherit: false)
                .Should().BeEmpty($"{t.Name} carries [RequireRecentAuth] at the class level — must be action-level only");
        }
    }

    // -----------------------------------------------------------------------
    // #28 — RequireRecentAuth attribute is never combined with AllowAnonymous
    // -----------------------------------------------------------------------

    [Fact]
    public void RequireRecentAuth_attribute_is_never_combined_with_AllowAnonymous()
    {
        var asm = typeof(ProjectCeres.Common.Authentication.RequireRecentAuthAttribute).Assembly;
        var actionMethods = asm.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        foreach (var m in actionMethods)
        {
            var hasRecentAuth = m.GetCustomAttributes(typeof(ProjectCeres.Common.Authentication.RequireRecentAuthAttribute), inherit: false).Any();
            if (!hasRecentAuth) continue;
            var hasAllowAnon = m.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false).Any();
            hasAllowAnon.Should().BeFalse($"{m.DeclaringType!.Name}.{m.Name} carries both [RequireRecentAuth] and [AllowAnonymous] — these are mutually exclusive");
        }
    }

    // -----------------------------------------------------------------------
    // #29 — Three existing MFA endpoints carry RequireRecentAuth attribute
    // -----------------------------------------------------------------------

    [Fact]
    public void Three_existing_MFA_endpoints_carry_RequireRecentAuth_attribute()
    {
        var t = typeof(ProjectCeres.Controllers.Api.MfaController);
        foreach (var name in new[] { "Enroll", "EnrollVerify", "RegenerateBackupCodes" })
        {
            var m = t.GetMethod(name);
            m.Should().NotBeNull($"MfaController must expose {name}");
            m!.GetCustomAttributes(typeof(ProjectCeres.Common.Authentication.RequireRecentAuthAttribute), inherit: false)
                .Should().NotBeEmpty($"MfaController.{name} must carry [RequireRecentAuth] (Stage 6c.2)");
        }
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

    // -----------------------------------------------------------------------
    // Stage 6.12 — EmailChangeController architecture tests
    // -----------------------------------------------------------------------

    [Fact]
    public void EmailChangeController_has_correct_attribute_matrix()
    {
        var type = typeof(ProjectCeres.Controllers.Api.EmailChangeController);

        // /request: [RequireRecentAuth] (which inherits AuthorizeAttribute and
        // registers the "RecentAuth" policy = RequireAuthenticatedUser + freshness gate).
        // A separate [Authorize] would be redundant and would trip the
        // singular-attribute lookup in Every_controller_action_declares_authorization_intent.
        var request = type.GetMethod("RequestChange");
        request.Should().NotBeNull();
        request!.GetCustomAttributes(true)
            .Any(a => a is RequireRecentAuthAttribute)
            .Should().BeTrue("RequestChange must carry [RequireRecentAuth]");
        request.GetCustomAttributes(true)
            .Any(a => a is AllowAnonymousAttribute)
            .Should().BeFalse("RequestChange must NOT be [AllowAnonymous]");

        // /confirm and /revoke: [AllowAnonymous] + [EnableRateLimiting(AuthLoginByIp)]
        foreach (var name in new[] { "ConfirmChange", "RevokeChange" })
        {
            var mi = type.GetMethod(name);
            mi.Should().NotBeNull($"EmailChangeController must define {name}");
            mi!.GetCustomAttributes(true)
                .Any(a => a is AllowAnonymousAttribute)
                .Should().BeTrue($"{name} must be [AllowAnonymous] — token IS the auth");
            mi.GetCustomAttributes(true)
                .OfType<EnableRateLimitingAttribute>()
                .Any(a => a.PolicyName == AuthRateLimitPolicies.AuthLoginByIp)
                .Should().BeTrue($"{name} must be [EnableRateLimiting(AuthLoginByIp)]");
        }
    }

    [Fact]
    public void EmailChangeController_does_not_call_PasswordHasher_directly()
    {
        // The controller depends only on EmailChangeService — never on Argon2idPasswordHasher
        // directly; hashing is the service's responsibility.
        var type = typeof(ProjectCeres.Controllers.Api.EmailChangeController);
        var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        fields.Select(f => f.FieldType)
            .Should().NotContain(typeof(ProjectCeres.Common.Authentication.Argon2idPasswordHasher),
                "the controller must delegate token hashing to EmailChangeService");
        fields.Select(f => f.FieldType)
            .Should().NotContain(typeof(ProjectCeres.Common.Authentication.EmailChangeTokenGenerator),
                "the controller must delegate token generation to EmailChangeService");
    }

    [Fact]
    public void EmailChangeService_methods_take_CancellationToken()
    {
        var type = typeof(ProjectCeres.Common.Authentication.EmailChangeService);
        var publicMethods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "RequestAsync" or "ConfirmAsync" or "RevokeAsync")
            .ToList();

        publicMethods.Should().HaveCount(3,
            "EmailChangeService must expose RequestAsync, ConfirmAsync, RevokeAsync");

        foreach (var m in publicMethods)
        {
            var lastParam = m.GetParameters().LastOrDefault();
            lastParam.Should().NotBeNull($"{m.Name} must have at least one parameter");
            lastParam!.ParameterType.Should().Be(typeof(CancellationToken),
                $"{m.Name} must end with CancellationToken so callers can propagate cancellation");
        }
    }

    [Fact]
    public void EmailChangeController_does_not_have_class_level_AllowAnonymous()
    {
        // Class-level [AllowAnonymous] would defeat the [Authorize] + [RequireRecentAuth]
        // gate on /request. Pin: every action declares its own auth intent.
        var type = typeof(ProjectCeres.Controllers.Api.EmailChangeController);
        type.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false)
            .Should().BeEmpty("EmailChangeController must NOT carry class-level [AllowAnonymous]");
    }

    // ── Stage 6.14: AuditLog architecture invariants ───────────────────────

    [Fact]
    public void IAuditLogWriter_has_exactly_one_production_implementation()
    {
        var impls = App.GetTypes()
            .Where(t => typeof(ProjectCeres.Common.Authentication.IAuditLogWriter).IsAssignableFrom(t)
                        && t is { IsClass: true, IsAbstract: false })
            .ToList();
        impls.Should().ContainSingle()
            .Which.Should().Be(typeof(ProjectCeres.Common.Authentication.AuditLogWriter));
    }

    [Fact]
    public void AuditLogAction_enum_values_match_documented_set()
    {
        // Pins the enum against docs/superpowers/specs/2026-05-11-stage-6-14-audit-log-design.md § 3.1.
        var expected = new[]
        {
            "LoginSucceeded", "LoginSucceededMfa", "LoginSucceededBackupCode",
            "Logout", "Registered",
            "PasswordResetRequested", "PasswordResetCompleted",
            "EmailChangeRequested", "EmailChangeConfirmed", "EmailChangeRevoked",
            "MfaEnrolled", "BackupCodesRegenerated",
            "MfaDisabled", "LockoutSelfServiceUnlock",
            "DataExportRequested", "GdprErasureRequested",
        };
        Enum.GetNames<ProjectCeres.Models.AuditLogAction>()
            .Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void AuditLog_entity_contains_no_financial_amount_columns()
    {
        // security-model.md line 554: "Financial amounts NEVER appear in audit entries."
        // Belt-and-braces: reflection-scan for forbidden names + decimal-typed properties.
        var forbidden = new[] { "Amount", "Balance", "Value", "Total" };
        var props = typeof(ProjectCeres.Models.AuditLog).GetProperties();

        props.Should().NotContain(p => forbidden.Contains(p.Name, StringComparer.OrdinalIgnoreCase),
            "AuditLog must never carry a financial-amount column.");

        props.Should().NotContain(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?),
            "AuditLog must never have a decimal property.");
    }

    // ── Stage 6.10: LockoutUnlockController architecture invariants ────────

    [Fact]
    public void LockoutUnlockController_action_has_AllowAnonymous()
    {
        var action = typeof(ProjectCeres.Controllers.Api.LockoutUnlockController)
            .GetMethod("Confirm")!;
        action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false)
            .Should().NotBeEmpty(
                "Confirm must be [AllowAnonymous] — pre-auth recovery endpoint, " +
                "documented in security-model.md § Global Authorization Policy");
    }

    [Fact]
    public void LockoutUnlockController_action_has_AuthLoginByIp_rate_limit()
    {
        var action = typeof(ProjectCeres.Controllers.Api.LockoutUnlockController)
            .GetMethod("Confirm")!;
        var rateLimit = action.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>()
            .SingleOrDefault();
        rateLimit.Should().NotBeNull("Confirm must declare a rate-limit policy");
        rateLimit!.PolicyName.Should().Be(ProjectCeres.Common.Authentication.AuthRateLimitPolicies.AuthLoginByIp,
            "Confirm reuses the existing AuthLoginByIp policy per Stage 6.10 D6");
    }

    [Fact]
    public void LockoutUnlockController_does_not_have_class_level_AllowAnonymous()
    {
        var type = typeof(ProjectCeres.Controllers.Api.LockoutUnlockController);
        type.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false)
            .Should().BeEmpty("Anonymity must be declared on the action, not the class");
    }
}
