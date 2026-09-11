using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Pins ADR-0078: import + reconciliation-review + transfer-review endpoints are
/// shelved from the beta (Production). They must be unreachable (404) in Production
/// and remain live in Development / E2E / Testing environments.
/// </summary>
[Collection("IntegrationParallel4")]
public class ShelvedEndpointFencingTests
{
    // A bare Production-environment factory. All settings required by Program.cs under
    // Production are supplied explicitly so the host builds without throwing.
    // Pattern mirrors E2eEnvironmentRegistrationTests.NonE2eFactory (Production path).
    //
    // Stage 12.18 — deliberately NOT a Bucket4Factory: these are GET-only 404-fencing
    // assertions against Program.cs environment routing; they read/write no app data, so
    // targeting the legacy DB rather than the bucket-4 clone carries no isolation risk.
    // The legacy DB is provisioned as a backstop by setup-test-db.sh for exactly such
    // uncollected/self-contained factories.
    private static WebApplicationFactory<Program> BuildProductionFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Stage75:SkipPrivilegeLeakCheck", "true");
            b.UseSetting("Email:SkipSupportAddressCheck", "true");
            b.UseSetting("Email:SkipPublicBaseUrlCheck", "true");
            b.UseSetting("ConnectionStrings:ApplicationConnection",
                "Host=localhost;Database=project_ceres_test;Username=ceres_app;Password=ceres_app_dev_password");
            b.UseSetting("ConnectionStrings:AdminConnection",
                "Host=localhost;Database=project_ceres_test;Username=ceres_admin;Password=ceres_admin_dev_password");
            b.UseSetting("ConnectionStrings:MigrationConnection",
                "Host=localhost;Database=project_ceres_test;Username=ceres_migrator;Password=ceres_migrator_dev_password");
            b.UseSetting("Authentication:TokenLookupSecret:Secret",
                Convert.ToBase64String(new byte[32]));
            b.UseSetting("Email:Resend:ApiKey", "re_test_key_for_fencing_test");
        });

    [Theory]
    [InlineData("/api/import")]
    [InlineData("/api/import/headers")]
    [InlineData("/api/import-profiles")]
    [InlineData("/api/reconciliation-review")]
    [InlineData("/api/transfer-review")]
    public async Task Shelved_import_and_review_endpoints_return_404_in_beta(string path)
    {
        // Factory configured with Production environment → beta posture (endpoints fenced).
        await using var factory = BuildProductionFactory();
        var client = factory.CreateClient();
        var resp = await client.GetAsync(path);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"import + Review are shelved from the beta (ADR-0078); {path} must be unreachable in Production");
    }
}
