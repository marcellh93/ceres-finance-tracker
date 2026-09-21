using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using Xunit;

namespace ProjectCeres.Tests.Unit;

public class UserContentEntitiesTests
{
    // Building DbContextOptions and reading .Model only triggers OnModelCreating —
    // it does not open a connection, so a placeholder Npgsql connection string is fine.
    private static AppDbContext Ctx() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options,
        new Common.FakeCurrentUserAccessor(Guid.Empty));

    [Fact]
    public void List_includes_support_tickets_but_excludes_security_tables()
    {
        var model = Ctx().Model;
        var names = UserContentEntities.List(model).Select(t => t.PostgresTableName).ToHashSet();

        // User content — must export
        names.Should().Contain("SupportTickets");
        names.Should().Contain("Accounts");

        // Security/auth-internal — excluded
        names.Should().NotContain("AuditLogs");
        names.Should().NotContain("FailedLoginAttempts");
        names.Should().NotContain("UserSessions");
        names.Should().NotContain("ExportJobs");
    }
}
