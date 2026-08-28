using System.IO;
using System.Reflection;
using FluentAssertions;
using ProjectCeres.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication;

public class ArchitectureTests
{
    private static readonly Assembly App = typeof(Program).Assembly;

    [Fact]
    public void No_controller_class_has_AllowAnonymous()
    {
        // All Razor controllers were deleted in Stage 11 Commit 2. Every remaining
        // controller must declare anonymity per-method to make the choice explicit
        // and reviewable.
        var violations = App.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(t => t.FullName!)
            .ToList();

        violations.Should().BeEmpty(
            "controllers must declare AllowAnonymous at the method level only — class-level lets new actions inherit anonymity by accident");
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
    public void HttpGet_actions_must_not_have_write_verb_names()
    {
        // All Razor controllers were deleted in Stage 11 Commit 2. Every remaining
        // [HttpGet] action is on the API surface where naming hygiene maps directly
        // to behaviour.
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
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<HttpGetAttribute>() is not null)
            .Where(m => forbiddenPrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        violations.Should().BeEmpty("GET endpoints must be side-effect-free per RFC 9110");
    }

    [Fact]
    public void FailedLoginAttempt_HasRequiredIndexes()
    {
        var factory = new AuthTestWebApplicationFactory();
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = db.Model.FindEntityType(typeof(FailedLoginAttempt));
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
        var type = typeof(Controllers.Api.PasswordResetController);
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
        var type = typeof(Controllers.Api.PasswordResetController);
        var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        fields.Select(f => f.FieldType)
            .Should().NotContain(typeof(Argon2idPasswordHasher),
                "the controller must delegate password hashing to PasswordResetService");
    }

    // -----------------------------------------------------------------------
    // #42 — Only known IEmailService implementations exist in the production assembly
    // -----------------------------------------------------------------------

    [Fact]
    public void IEmailService_impls_are_LogOnly_Noop_or_Resend()
    {
        // LogOnlyEmailService is the dev fallback (registered when Email:Resend:ApiKey
        // is unbound and Environment != Production). ResendEmailService is the production
        // impl (registered when Email:Resend:ApiKey is bound) — wired in Stage 8c.
        // NoopEmailService ships in the production assembly (test-helper role) but must
        // never be registered in production DI. Any FOURTH implementation must be
        // explicitly added here, with the same gating rigor Stage 8 applied.
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
            typeof(ResendEmailService),
            typeof(FileSinkEmailService), // Stage 9.11 — E2E-only sink
        };

        impls.Should().OnlyContain(t => knownImpls.Contains(t),
            "only LogOnlyEmailService, NoopEmailService, and ResendEmailService may exist " +
            "in the production assembly — any new implementation must be added to knownImpls " +
            "explicitly so the addition is reviewed.");

        impls.Should().Contain(typeof(LogOnlyEmailService),
            "LogOnlyEmailService must be present as the dev IEmailService implementation");

        impls.Should().Contain(typeof(ResendEmailService),
            "ResendEmailService must be present as the production IEmailService implementation (Stage 8c)");
    }

    // -----------------------------------------------------------------------
    // Stage 9.11 §5.4 — only known IBreachedPasswordChecker impls exist
    // (mirror of IEmailService_impls_are_LogOnly_Noop_or_Resend)
    // -----------------------------------------------------------------------

    [Fact]
    public void IBreachedPasswordChecker_impls_are_Hibp_or_E2eStub()
    {
        // HaveIBeenPwnedPasswordChecker is the real NIST breach-screening impl (registered
        // via AddHttpClient outside E2E). AlwaysAllowBreachedPasswordChecker is the E2E-only
        // stub that treats every password as not-breached — registered ONLY under
        // ASPNETCORE_ENVIRONMENT=E2E. Any THIRD implementation must be added here
        // explicitly: a new checker that silently disables NIST breach screening must be
        // reviewed with the same rigor before it can exist in the production assembly.
        var asm = typeof(IBreachedPasswordChecker).Assembly;
        var impls = asm.GetTypes()
            .Where(t => typeof(IBreachedPasswordChecker).IsAssignableFrom(t)
                     && !t.IsAbstract
                     && !t.IsInterface)
            .ToList();

        var knownCheckers = new[]
        {
            typeof(HaveIBeenPwnedPasswordChecker),
            typeof(AlwaysAllowBreachedPasswordChecker), // Stage 9.11 — E2E-only stub
        };

        impls.Should().OnlyContain(t => knownCheckers.Contains(t),
            "only HaveIBeenPwnedPasswordChecker and AlwaysAllowBreachedPasswordChecker may " +
            "exist in the production assembly — any new implementation must be added to " +
            "knownCheckers explicitly so a checker that silently disables NIST breach " +
            "screening gets reviewed before it ships.");

        impls.Should().Contain(typeof(HaveIBeenPwnedPasswordChecker),
            "HaveIBeenPwnedPasswordChecker must be present as the real breach-screening checker");
    }

    // -----------------------------------------------------------------------
    // #43 — PasswordResetService public methods accept CancellationToken
    // -----------------------------------------------------------------------

    [Fact]
    public void PasswordResetService_methods_take_CancellationToken()
    {
        var type = typeof(PasswordResetService);
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
        var asm = typeof(RequireRecentAuthAttribute).Assembly;
        var controllerTypes = asm.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var t in controllerTypes)
        {
            t.GetCustomAttributes(typeof(RequireRecentAuthAttribute), inherit: false)
                .Should().BeEmpty($"{t.Name} carries [RequireRecentAuth] at the class level — must be action-level only");
        }
    }

    // -----------------------------------------------------------------------
    // #28 — RequireRecentAuth attribute is never combined with AllowAnonymous
    // -----------------------------------------------------------------------

    [Fact]
    public void RequireRecentAuth_attribute_is_never_combined_with_AllowAnonymous()
    {
        var asm = typeof(RequireRecentAuthAttribute).Assembly;
        var actionMethods = asm.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        foreach (var m in actionMethods)
        {
            var hasRecentAuth = m.GetCustomAttributes(typeof(RequireRecentAuthAttribute), inherit: false).Any();
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
        var t = typeof(Controllers.Api.MfaController);
        foreach (var name in new[] { "Enroll", "EnrollVerify", "RegenerateBackupCodes" })
        {
            var m = t.GetMethod(name);
            m.Should().NotBeNull($"MfaController must expose {name}");
            m!.GetCustomAttributes(typeof(RequireRecentAuthAttribute), inherit: false)
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
            throw new FileNotFoundException($"Could not resolve controller source: {resolved}");
        return resolved;
    }

    // -----------------------------------------------------------------------
    // Stage 6.12 — EmailChangeController architecture tests
    // -----------------------------------------------------------------------

    [Fact]
    public void EmailChangeController_has_correct_attribute_matrix()
    {
        var type = typeof(Controllers.Api.EmailChangeController);

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
        var type = typeof(Controllers.Api.EmailChangeController);
        var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        fields.Select(f => f.FieldType)
            .Should().NotContain(typeof(Argon2idPasswordHasher),
                "the controller must delegate token hashing to EmailChangeService");
        fields.Select(f => f.FieldType)
            .Should().NotContain(typeof(EmailChangeTokenGenerator),
                "the controller must delegate token generation to EmailChangeService");
    }

    [Fact]
    public void EmailChangeService_methods_take_CancellationToken()
    {
        var type = typeof(EmailChangeService);
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
        var type = typeof(Controllers.Api.EmailChangeController);
        type.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false)
            .Should().BeEmpty("EmailChangeController must NOT carry class-level [AllowAnonymous]");
    }

    // ── Stage 6.14: AuditLog architecture invariants ───────────────────────

    [Fact]
    public void IAuditLogWriter_has_exactly_one_production_implementation()
    {
        var impls = App.GetTypes()
            .Where(t => typeof(IAuditLogWriter).IsAssignableFrom(t)
                        && t is { IsClass: true, IsAbstract: false })
            .ToList();
        impls.Should().ContainSingle()
            .Which.Should().Be(typeof(AuditLogWriter));
    }

    [Fact]
    public void Email_triggering_endpoints_carry_a_rate_limit()
    {
        // security-model.md § Email Security Rules: "Rate-limit ALL email-triggering
        // endpoints ... to prevent the app from being used as a spam relay."
        //
        // A reflection test rather than a 429 test on purpose. The WAF stubs every
        // limiter to a no-op so unrelated suites do not trip on burst, so a live 429
        // assertion would need its own factory and would still only cover one endpoint.
        // This asserts the thing that actually regresses: someone adds a mail-sending
        // action and forgets the attribute.
        //
        // POST /api/support/tickets shipped unlimited in its first draft (2026-08-27) and
        // was caught in review, not by a test. This is that test.
        var mailSendingActions = new (Type Controller, string Action)[]
        {
            (typeof(SupportApiController), "Create"),
            // Stage 12.6: the user-reply endpoint now notifies the operator, so it is a
            // mail-sending action and must carry the same rate limit as Create.
            (typeof(SupportApiController), "Reply"),
        };

        foreach (var (controller, actionName) in mailSendingActions)
        {
            var action = controller.GetMethod(actionName)
                ?? throw new InvalidOperationException($"{controller.Name}.{actionName} not found");

            var hasUserLimit = action.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Length > 0;
            var hasIpLimit = action.GetCustomAttributes(typeof(ApplyEmailIpRateLimitAttribute), true).Length > 0;

            hasUserLimit.Should().BeTrue(
                $"{controller.Name}.{actionName} sends email and needs a per-user rate limit");
            hasIpLimit.Should().BeTrue(
                $"{controller.Name}.{actionName} sends email and needs the per-IP backstop");
        }
    }

    [Fact]
    public void AuditLogAction_enum_values_match_documented_set()
    {
        // Pins the enum against docs/superpowers/specs/2026-05-11-stage-6-14-audit-log-design.md § 3.1.
        // Stage 9.3 (2026-05-22) added EmailVerificationRequested + EmailVerified for the
        // registration confirmation flow — both wired by EmailConfirmationService.
        // Stage 12.5 (2026-08-27) added SupportTicketCreated, wired by SupportApiController.
        var expected = new[]
        {
            "LoginSucceeded", "LoginSucceededMfa", "LoginSucceededBackupCode",
            "Logout", "Registered",
            "PasswordResetRequested", "PasswordResetCompleted",
            "EmailChangeRequested", "EmailChangeConfirmed", "EmailChangeRevoked",
            "MfaEnrolled", "BackupCodesRegenerated",
            "MfaDisabled", "LockoutSelfServiceUnlock",
            "DataExportRequested", "GdprErasureRequested",
            "EmailVerificationRequested", "EmailVerified",
            "SupportTicketCreated",
        };
        Enum.GetNames<AuditLogAction>()
            .Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void AuditLog_entity_contains_no_financial_amount_columns()
    {
        // security-model.md line 554: "Financial amounts NEVER appear in audit entries."
        // Belt-and-braces: reflection-scan for forbidden names + decimal-typed properties.
        var forbidden = new[] { "Amount", "Balance", "Value", "Total" };
        var props = typeof(AuditLog).GetProperties();

        props.Should().NotContain(p => forbidden.Contains(p.Name, StringComparer.OrdinalIgnoreCase),
            "AuditLog must never carry a financial-amount column.");

        props.Should().NotContain(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?),
            "AuditLog must never have a decimal property.");
    }

    // ── Stage 6.10: LockoutUnlockController architecture invariants ────────

    [Fact]
    public void LockoutUnlockController_action_has_AllowAnonymous()
    {
        var action = typeof(Controllers.Api.LockoutUnlockController)
            .GetMethod("Confirm")!;
        action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false)
            .Should().NotBeEmpty(
                "Confirm must be [AllowAnonymous] — pre-auth recovery endpoint, " +
                "documented in security-model.md § Global Authorization Policy");
    }

    [Fact]
    public void LockoutUnlockController_action_has_AuthLoginByIp_rate_limit()
    {
        var action = typeof(Controllers.Api.LockoutUnlockController)
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
        var type = typeof(Controllers.Api.LockoutUnlockController);
        type.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false)
            .Should().BeEmpty("Anonymity must be declared on the action, not the class");
    }

    // ── Stage 7: IgnoreQueryFilters allow-list + global query filter pin ────

    /// <summary>
    /// Scans every .cs file under projectCeresRoot for actual IgnoreQueryFilters( call
    /// sites (not comment mentions) and returns the list of files that contain one.
    /// </summary>
    private static IReadOnlyList<string> ScanIgnoreQueryFiltersCallSites(string projectCeresRoot)
    {
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(projectCeresRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("/Migrations/")) continue;
            var lines = File.ReadAllLines(file);
            foreach (var raw in lines)
            {
                var line = raw.TrimStart();
                if (line.StartsWith("//")) continue;        // line comment
                if (line.StartsWith("*"))  continue;         // block comment continuation
                // The call site must include the opening paren, e.g. `.IgnoreQueryFilters()`.
                if (line.Contains("IgnoreQueryFilters("))
                {
                    hits.Add(file);
                    break; // one hit per file is enough
                }
            }
        }
        return hits;
    }

    [Fact]
    public void EnterAs_only_called_inside_BackgroundJobScope()
    {
        // Stage 7.5 Commit 6: `IUserScope.EnterAs(...)` is the primitive that sets the
        // current-user AsyncLocal. Every background-job entry point must route through
        // BackgroundJobScope.RunAsync (which holds the Guid.Empty doorway refusal and
        // logs job-name on failure). The only file allowed to call `EnterAs(...)` is
        // BackgroundJobScope itself — anything else risks bypassing the refusal.
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "ProjectCeres/Common/BackgroundJobScope.cs", // the doorway implementation
        };

        var repoRoot = FindRepoRoot();
        var projectCeres = Path.Combine(repoRoot, "ProjectCeres");
        var hits = ScanEnterAsCallSites(projectCeres);

        var unexpected = hits
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .Where(rel => !allowed.Contains(rel))
            .ToList();

        unexpected.Should().BeEmpty(
            "IUserScope.EnterAs() must only be called from BackgroundJobScope. " +
            "Every other background-job entry point must route through IBackgroundJobScope.RunAsync " +
            "to inherit the Guid.Empty doorway refusal and job-name logging.");
    }

    private static IReadOnlyList<string> ScanEnterAsCallSites(string projectCeresRoot)
    {
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(projectCeresRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("/Migrations/")) continue;
            var lines = File.ReadAllLines(file);
            foreach (var raw in lines)
            {
                var line = raw.TrimStart();
                if (line.StartsWith("//")) continue;
                if (line.StartsWith("*"))  continue;
                // Match `.EnterAs(` to avoid matching the interface declaration `EnterAs(Guid)`.
                if (line.Contains(".EnterAs("))
                {
                    hits.Add(file);
                    break;
                }
            }
        }
        return hits;
    }

    /// <summary>
    /// Walks up from the test bin directory to the repository root (identified by ProjectCeres.sln).
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ProjectCeres.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void IgnoreQueryFilters_only_appears_in_documented_exception_paths()
    {
        // Stage 7 safety net: every IgnoreQueryFilters() call site must be in the allow-list
        // or under the pre-granted ProjectCeres/Admin/ surface (Phase 4+).
        // Adding the call anywhere else bypasses multi-tenancy isolation — the test catches it.
        // Each allow-listed file performs a legitimately cross-tenant read. The trailing
        // justification comments let a reader audit the allow-list without grepping the
        // codebase — keep them in lockstep with the production code.
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "ProjectCeres/Common/UserJobRunner.cs",                                   // enumerates AspNetUsers for per-user fan-out background jobs
            "ProjectCeres/Services/CategorySeedService.cs",                           // idempotency check at registration; user not yet authenticated in scope
            "ProjectCeres/Common/Authentication/PasswordResetService.cs",             // token verify before the user is authenticated (no cookie yet)
            "ProjectCeres/Common/Authentication/EmailChangeService.cs",               // token verify before the user is authenticated (no cookie yet)
            "ProjectCeres/Common/Authentication/LockoutUnlockService.cs",             // unlock token candidate scan before the user is authenticated
            "ProjectCeres/Common/Authentication/EmailConfirmationService.cs",         // email-verification token verify before the user is authenticated (Stage 9.3)
            "ProjectCeres/Common/Authentication/MfaBackupCodeService.cs",             // MFA-pending step: principal not yet committed to the cookie
            "ProjectCeres/Common/Authentication/SessionRevocationValidator.cs",       // runs during cookie validation, before HTTP principal is committed
            "ProjectCeres/Common/Authentication/PersistentCookieRotationMiddleware.cs", // runs before UseAuthentication; resolves session by cookie-embedded id
            "ProjectCeres/Common/Authentication/TotpReplayGuard.cs",                  // MFA-pending step + cross-tenant retention purge of expired entries
            "ProjectCeres/Common/Email/EmailRecipientResolver.cs",                    // Stage 8a: resolves To: from ApplicationUser.Email; callers include pre-auth flows (password reset, lockout) where the user is not yet signed in
            "ProjectCeres/Common/Email/LanguageResolver.cs",                          // Stage 8b: pre-auth flows may read Settings.Language before the user has signed in (password reset, lockout-unlock)
            "ProjectCeres/Controllers/Api/ResendWebhookController.cs",                // Stage 8e: Resend webhook is pre-auth (Svix HMAC IS the auth) and cross-tenant by design — looks up users by NormalizedEmail to attach an optional UserId to the recorded event
        };

        var repoRoot = FindRepoRoot();
        var projectCeres = Path.Combine(repoRoot, "ProjectCeres");
        var hits = ScanIgnoreQueryFiltersCallSites(projectCeres);

        var unexpected = hits
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .Where(rel => !allowed.Contains(rel) && !rel.StartsWith("ProjectCeres/Admin/", StringComparison.Ordinal))
            .ToList();

        unexpected.Should().BeEmpty(
            "IgnoreQueryFilters() bypasses Stage 7's multi-tenancy safety net. " +
            "Either move the call into one of the allow-listed services OR add the file to the allow-list " +
            "in ArchitectureTests.cs with an inline justification comment.");
    }

    [Fact]
    public void Every_user_owned_entity_carries_a_global_query_filter()
    {
        // Stage 7 Task 9 wired HasQueryFilter on 20 entity types (Movement covers its
        // concrete subtypes Transaction/Transfer/LiabilityPayment via TPC inheritance).
        // This test pins that contract: any entity removed from ConfigureGlobalQueryFilters
        // breaks the build immediately.
        var factory = new AuthTestWebApplicationFactory();
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Direct-filtered entities (filter declared on the entity type itself).
            // Movement is the TPC root — its filter propagates to Transaction/Transfer/LiabilityPayment.
            var expectedDirect = new[]
            {
                typeof(Account),
                typeof(Budget),
                typeof(Category),
                typeof(CategoryBudget),
                typeof(ImportProfile),
                typeof(ImportStagedTransaction),
                typeof(ImportStagedTransfer),
                typeof(ImportTransferExclusion),
                typeof(RecurringTransaction),
                typeof(SavedReport),
                typeof(Settings),
                typeof(Movement),  // TPC root — filter propagates to subtypes
                typeof(UserSession),
                typeof(UserBlockedIp),
                typeof(UserMfaBackupCode),
                typeof(TotpReplayEntry),
                typeof(PasswordResetToken),
                typeof(EmailChangeToken),
                typeof(LockoutUnlockToken),
                typeof(AuditLog),
            };

            var missing = expectedDirect
                .Where(t => !(db.Model.FindEntityType(t)?.GetDeclaredQueryFilters().Any() ?? false))
                .Select(t => t.Name)
                .ToList();

            missing.Should().BeEmpty(
                "Stage 7 requires a global query filter on every user-owned entity. " +
                "Missing entities here mean Task 9's ConfigureGlobalQueryFilters needs the entity added.");
        }
        finally
        {
            factory.Dispose();
        }
    }

    [Fact]
    public void UserOwnedModel_RlsTables_match_HasQueryFilter_registrations()
    {
        // Stage 7.6.4 (repointed to UserOwnedModel in Stage 9.5b): pin that every
        // user-owned RLS table has a HasQueryFilter registration on the runtime EF model
        // (either directly, or via TPC inheritance from a base type that has the filter —
        // the Transaction/Transfer/LiabilityPayment case, where Movement carries the
        // filter). The model-derived UserOwnedModel.RlsTables is the source of truth;
        // ConfigureGlobalQueryFilters loops it via a generic helper. This test fails the
        // build if any future user-owned entity slips through without an EF-side filter.
        //
        // The exclusion list that used to live here was removed on 2026-08-23. It skipped
        // TransactionAttachments and TransferAttachments on the grounds that they "carry no
        // UserId at the EF layer and are scoped via their parent in service code". That was
        // true when written, and Stage 7.5 Phase A superseded it by adding a denormalized
        // UserId to both — so for roughly fifteen months this test was silently skipping two
        // tables that do carry filters. Every user-owned table is now checked, which is what
        // the test claims to do.
        var factory = new AuthTestWebApplicationFactory();
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var missing = ProjectCeres.Common.UserOwnedModel.RlsTables(db.Model)
                .Where(t => !HasFilterOnSelfOrBase(db.Model.FindEntityType(t.EntityType)))
                .Select(t => t.EntityType.Name)
                .ToList();

            missing.Should().BeEmpty(
                "Every user-owned RLS table must have a " +
                "HasQueryFilter registration on the EF model (directly or via TPC " +
                "inheritance). Stage 7.5's RLS parity test catches the migration-side " +
                "drift; this test catches the EF-side drift. Add the missing entity to " +
                "ConfigureGlobalQueryFilters in AppDbContext.cs.");
        }
        finally
        {
            factory.Dispose();
        }

        static bool HasFilterOnSelfOrBase(IEntityType? et)
        {
            while (et is not null)
            {
                if (et.GetDeclaredQueryFilters().Any())
                    return true;
                et = et.BaseType;
            }
            return false;
        }
    }

    [Fact]
    public void FailedLoginAttempt_has_no_global_query_filter()
    {
        // ADR-0067: FailedLoginAttempt is cross-tenant by design. The retention sweep iterates
        // all rows regardless of user. UserId is nullable; users themselves are never queried
        // by UserId on this table (queries are by IpAddress or EmailAttempted).
        var factory = new AuthTestWebApplicationFactory();
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Model.FindEntityType(typeof(FailedLoginAttempt))!
                .GetDeclaredQueryFilters().Should().BeEmpty(
                    "FailedLoginAttempt must remain filter-free per ADR-0067 — retention sweep is cross-tenant by design.");
        }
        finally
        {
            factory.Dispose();
        }
    }

    // Stage 7.6.7 / ADR-0073: every [AllowAnonymous] action on an [ApiController] type
    // either carries a [PreAuthCallSite] tag (so HttpContextCurrentUserAccessor can resolve
    // it as UserContext.PreAuth) or appears in the explicit no-DB allow-list. Replaces the
    // prior IPreAuthCallSiteTagger string registry; new pre-auth routes become a
    // compile-time addition (one attribute on the action), not a registry edit elsewhere.
    [Fact]
    public void Every_anonymous_api_action_carries_a_PreAuthCallSite_attribute_or_is_in_the_no_db_allow_list()
    {
        // Allow-list: actions that are [AllowAnonymous] but never touch the DB. These don't
        // need a UserContext.PreAuth tag because the RowLevelSecurityInterceptor never
        // fires for them (no AppDbContext in the request pipeline). Adding to this list
        // requires the action's body to genuinely not open a DB connection.
        var noDbAllowList = new HashSet<string>(StringComparer.Ordinal)
        {
            "ProjectCeres.Controllers.Api.HealthApiController.Get",
            "ProjectCeres.Controllers.Api.HealthApiController.Validate",
            // AuthController.Csrf issues an antiforgery cookie — IAntiforgery.GetAndStoreTokens
            // doesn't open a DB connection.
            "ProjectCeres.Controllers.Api.AuthController.Csrf",
        };

        var controllers = App.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<ApiControllerAttribute>() is not null);

        var violations = new List<string>();

        foreach (var controller in controllers)
        {
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<AllowAnonymousAttribute>() is null)
                    continue;

                var fullName = $"{controller.FullName}.{method.Name}";
                if (noDbAllowList.Contains(fullName))
                    continue;

                if (method.GetCustomAttribute<ProjectCeres.Common.PreAuthCallSiteAttribute>() is null)
                    violations.Add(fullName);
            }
        }

        violations.Should().BeEmpty(
            "every [AllowAnonymous] action on an [ApiController] must carry [PreAuthCallSite(\"<name>\")] " +
            "(or be added to the explicit no-DB allow-list inside this test). The attribute lets " +
            "HttpContextCurrentUserAccessor resolve the request as UserContext.PreAuth instead of " +
            "Background, which keeps the RowLevelSecurityInterceptor's diagnostic clean.");
    }

    [Fact]
    public void Admin_namespace_is_allow_listed_for_IgnoreQueryFilters()
    {
        // ADR-0065 reserves Admin/ as the only namespace permitted to bypass the
        // per-user query filter. Stage 15.6 is the first code to live there, so this
        // pins that a file under Admin/ is accepted while the boundary still holds
        // for every other namespace.
        var repoRoot = FindRepoRoot();
        var adminDir = Path.Combine(repoRoot, "ProjectCeres", "Admin");

        Directory.Exists(adminDir).Should().BeTrue(
            "ProjectCeres/Admin/ is the namespace ADR-0065 reserves for cross-user queries");

        File.Exists(Path.Combine(adminDir, "README.md")).Should().BeTrue(
            "the boundary needs a stated contract, not just a directory");
    }
}
