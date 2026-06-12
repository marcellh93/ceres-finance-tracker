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
}
