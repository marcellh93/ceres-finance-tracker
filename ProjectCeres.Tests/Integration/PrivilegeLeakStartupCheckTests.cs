using FluentAssertions;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Stage 7.5 / ADR-0068 — pins the contract of <see cref="PrivilegeLeakStartupCheck"/>:
/// returns silently when the connection lacks DDL, throws when it has DDL. Hits the
/// real <c>project_ceres_test</c> database under both <c>ceres_app</c> (no DDL) and
/// <c>ceres_migrator</c> (DDL) roles.
/// </summary>
public class PrivilegeLeakStartupCheckTests
{
    [Fact]
    public async Task Passes_silently_when_connection_is_ceres_app()
    {
        await PrivilegeLeakStartupCheck
            .EnsureApplicationConnectionLacksDdlAsync(TestDbFixture.AppConnectionString);
    }

    [Fact]
    public async Task Throws_when_connection_is_ceres_migrator()
    {
        var act = () => PrivilegeLeakStartupCheck
            .EnsureApplicationConnectionLacksDdlAsync(TestDbFixture.MigratorConnectionString);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*wired to a privileged role*");
    }

    [Fact]
    public async Task Passes_silently_when_connection_is_ceres_admin()
    {
        // ceres_admin has BYPASSRLS but no DDL, so the CREATE fails with 42501 and the
        // check passes. This pins that the check is specifically about DDL rights, not
        // about BYPASSRLS — those are separate failure modes. (BYPASSRLS-via-misconfig
        // is a real risk on its own but the runtime can't probe for it the same way:
        // there's no SQL that distinguishes BYPASSRLS from NOBYPASSRLS without writing
        // to an RLS-bound table, and at startup no such tables exist yet for the user.)
        await PrivilegeLeakStartupCheck
            .EnsureApplicationConnectionLacksDdlAsync(TestDbFixture.AdminConnectionString);
    }
}
