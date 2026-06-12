using FluentAssertions;
using ProjectCeres.Common;
using Xunit;

namespace ProjectCeres.Tests.Integration.Configuration;

// Behaviour tests for the E2E DB boot guard. The guard fail-closes: under
// ASPNETCORE_ENVIRONMENT=E2E, Program.cs refuses to start unless the application
// connection points at project_ceres_e2e. Needs a live local Postgres (same one
// the integration suite uses). The DI registration tests prove the WIRING; these
// prove the GUARD'S throw — the path no other test exercises.
public class E2eDatabaseGuardStartupCheckTests
{
    private const string NonE2eConnection =
        "Host=localhost;Database=project_ceres_test;Username=ceres_app;Password=ceres_app_dev_password";

    private const string E2eConnection =
        "Host=localhost;Database=project_ceres_e2e;Username=ceres_app;Password=ceres_app_dev_password";

    [Fact]
    public async Task Guard_throws_when_connected_to_a_non_e2e_database()
    {
        // Connected to project_ceres_test (NOT project_ceres_e2e) → fail-closed: the guard
        // must throw rather than let an E2E boot proceed against a non-e2e database (which
        // would file-sink auth tokens, raise rate limits, and disable breach screening
        // against whatever real data the connection points at).
        var act = async () =>
            await E2eDatabaseGuardStartupCheck.EnsureConnectedToE2eDatabaseAsync(NonE2eConnection);

        (await act.Should().ThrowAsync<InvalidOperationException>(
                "the guard must refuse to start the E2E environment against a non-e2e database"))
            .Which.Message.Should().Contain(E2eDatabaseGuardStartupCheck.ExpectedDatabase,
                "the throw must name the expected database (project_ceres_e2e) so the operator can diagnose the misconfiguration");
    }

    [Fact]
    public async Task Guard_passes_when_connected_to_project_ceres_e2e()
    {
        // Positive case: connected to project_ceres_e2e → no throw. project_ceres_e2e exists
        // on this dev machine and in CI (created by the E2E setup). If it is ever absent the
        // negative test above is the load-bearing one; this case confirms the guard does not
        // false-positive against the correct database.
        var act = async () =>
            await E2eDatabaseGuardStartupCheck.EnsureConnectedToE2eDatabaseAsync(E2eConnection);

        await act.Should().NotThrowAsync(
            "the guard must allow startup when the application connection points at project_ceres_e2e");
    }
}
