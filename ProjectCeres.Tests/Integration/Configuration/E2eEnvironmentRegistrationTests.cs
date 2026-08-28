using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using Xunit;

namespace ProjectCeres.Tests.Integration.Configuration;

public class E2eEnvironmentRegistrationTests
{
    // Boot Program under E2E. The DB guard is skipped (we test DI wiring, not the guard,
    // which needs a live e2e DB); connections point at the existing test DB so Build() works.
    private static WebApplicationFactory<Program> E2eFactory(bool withResendKey) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("E2E");
            b.UseSetting("Stage75:SkipPrivilegeLeakCheck", "true");
            b.UseSetting("Email:SkipSupportAddressCheck", "true");
            b.UseSetting("Email:SkipPublicBaseUrlCheck", "true");
            b.UseSetting("E2E:SkipDatabaseGuard", "true");
            b.UseSetting("ConnectionStrings:ApplicationConnection",
                "Host=localhost;Database=project_ceres_test;Username=ceres_app;Password=ceres_app_dev_password");
            b.UseSetting("ConnectionStrings:AdminConnection",
                "Host=localhost;Database=project_ceres_test;Username=ceres_admin;Password=ceres_admin_dev_password");
            b.UseSetting("Authentication:TokenLookupSecret:Secret",
                Convert.ToBase64String(new byte[32]));
            b.UseSetting("Email:FileSink:Directory",
                Path.Combine(Path.GetTempPath(), $"ceres-e2e-ditest-{Guid.NewGuid():N}"));
            if (withResendKey)
                b.UseSetting("Email:Resend:ApiKey", "re_test_key_should_be_ignored_under_e2e");
        });

    // Boot Program under a NON-E2E environment (Development / Production / Staging) to pin
    // the negative: the E2E-only stubs (FileSinkEmailService, AlwaysAllowBreachedPasswordChecker)
    // must NEVER resolve outside E2E. No E2E:SkipDatabaseGuard here — that flag is E2E-only and
    // the guard only fires under E2E anyway. Stage75:SkipPrivilegeLeakCheck still skips the
    // startup DDL/RLS-parity probe so Build() doesn't open a live admin connection per test.
    // .NET would load user-secrets under Development, but explicit UseSetting overrides win,
    // so the test DB connection strings + token secret apply regardless of environment.
    // The Resend key is set EXPLICITLY on both paths (a key on the with-key path; empty on the
    // no-key path) precisely BECAUSE Development loads user-secrets — a dev machine whose
    // secrets store carries Email:Resend:ApiKey would otherwise resolve Resend on the
    // "no key" path and make the LogOnly pin machine-dependent. Empty string still hits the
    // string.IsNullOrWhiteSpace branch in Program.cs, so the no-key path is deterministic.
    private static WebApplicationFactory<Program> NonE2eFactory(string environment, bool withResendKey) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Stage75:SkipPrivilegeLeakCheck", "true");
            b.UseSetting("Email:SkipSupportAddressCheck", "true");
            b.UseSetting("Email:SkipPublicBaseUrlCheck", "true");
            b.UseSetting("ConnectionStrings:ApplicationConnection",
                "Host=localhost;Database=project_ceres_test;Username=ceres_app;Password=ceres_app_dev_password");
            b.UseSetting("ConnectionStrings:AdminConnection",
                "Host=localhost;Database=project_ceres_test;Username=ceres_admin;Password=ceres_admin_dev_password");
            b.UseSetting("Authentication:TokenLookupSecret:Secret",
                Convert.ToBase64String(new byte[32]));
            b.UseSetting("Email:Resend:ApiKey", withResendKey ? "re_test_key_present" : "");
        });

    [Fact]
    public void Under_E2E_IEmailService_is_FileSink_even_with_a_Resend_key_present()
    {
        using var factory = E2eFactory(withResendKey: true);
        using var scope = factory.Services.CreateScope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        email.Should().BeOfType<FileSinkEmailService>();
    }

    [Fact]
    public void Under_E2E_IBreachedPasswordChecker_is_the_always_allow_stub()
    {
        using var factory = E2eFactory(withResendKey: false);
        using var scope = factory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IBreachedPasswordChecker>();
        checker.Should().BeOfType<AlwaysAllowBreachedPasswordChecker>();
    }

    // ── Negative pins: the E2E-only stubs must NEVER resolve outside E2E ──────
    // Each fails if the IsEnvironment("E2E") guard in Program.cs were removed —
    // the email/checker registration would then fall through to the E2E branch.

    [Fact]
    public void Under_Development_IEmailService_is_not_FileSink()
    {
        // Development + no Resend key → the dev fallback (LogOnlyEmailService),
        // NOT the E2E file sink. If the E2E guard were dropped, FileSink would resolve.
        using var factory = NonE2eFactory("Development", withResendKey: false);
        using var scope = factory.Services.CreateScope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        email.Should().BeOfType<LogOnlyEmailService>();
        email.Should().NotBeOfType<FileSinkEmailService>(
            "the file sink is E2E-only — Development must use the LogOnly fallback");
    }

    [Fact]
    public void Under_Production_IEmailService_is_Resend_not_FileSink()
    {
        // Production WITH a Resend key → ResendEmailService (Production with NO key throws,
        // so we supply a key to hit the resolve path). The load-bearing assertion is
        // NotBeOfType<FileSinkEmailService>: the sink must never resolve outside E2E.
        using var factory = NonE2eFactory("Production", withResendKey: true);
        using var scope = factory.Services.CreateScope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        email.Should().BeOfType<ResendEmailService>();
        email.Should().NotBeOfType<FileSinkEmailService>(
            "the file sink is E2E-only — Production must use Resend");
    }

    [Fact]
    public void Under_Development_IBreachedPasswordChecker_is_the_real_checker()
    {
        // Outside E2E the checker is the real HaveIBeenPwnedPasswordChecker (registered via
        // AddHttpClient), NOT the always-allow stub that disables NIST breach screening.
        using var factory = NonE2eFactory("Development", withResendKey: false);
        using var scope = factory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IBreachedPasswordChecker>();
        checker.Should().BeOfType<HaveIBeenPwnedPasswordChecker>();
        checker.Should().NotBeOfType<AlwaysAllowBreachedPasswordChecker>(
            "the always-allow stub is E2E-only — Development must use the real HIBP checker");
    }
}
