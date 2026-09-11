using FluentAssertions;
using Npgsql;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class AppRoleFixtureGuardTests
{
    private readonly AppRoleFixture _fixture;
    public AppRoleFixtureGuardTests(AppRoleFixture fixture) => _fixture = fixture;

    // Intentional duplicate of the fixture's InitializeAsync check: fixture fails the collection fast, this is the visible named green/red signal — don't deduplicate.
    [Fact]
    public async Task Ceres_app_role_does_not_have_BYPASSRLS()
    {
        await using var conn = new NpgsqlConnection(AppRoleFixture.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rolbypassrls FROM pg_roles WHERE rolname = 'ceres_app'";
        var bypass = (bool)(await cmd.ExecuteScalarAsync() ?? true);
        bypass.Should().BeFalse("ceres_app must be NOBYPASSRLS for the AppRole suite to mean anything");
    }
}
