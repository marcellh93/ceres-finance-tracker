using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using Xunit;

namespace ProjectCeres.Tests.Unit;

public class UserOwnedModelTests
{
    // Building DbContextOptions and reading .Model only triggers OnModelCreating —
    // it does not open a connection, so a placeholder Npgsql connection string is fine.
    private static AppDbContext Ctx() => new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options,
        new ProjectCeres.Tests.Common.FakeCurrentUserAccessor(Guid.Empty));

    [Fact]
    public void RlsTables_includes_the_three_concrete_Movement_tables_and_excludes_the_abstract_root()
    {
        var names = UserOwnedModel.RlsTables(Ctx().Model).Select(t => t.PostgresTableName).ToList();
        names.Should().Contain(new[] { "Transactions", "Transfers", "LiabilityPayments" });
        names.Should().NotContain("Movements");
        names.Should().NotContain("Movement");
    }

    [Fact]
    public void RlsTables_includes_both_attachment_tables()
    {
        var names = UserOwnedModel.RlsTables(Ctx().Model).Select(t => t.PostgresTableName).ToList();
        names.Should().Contain(new[] { "TransactionAttachments", "TransferAttachments" });
    }

    [Fact]
    public void RlsTables_excludes_entities_that_have_a_UserId_but_do_not_implement_IUserOwned()
    {
        var names = UserOwnedModel.RlsTables(Ctx().Model).Select(t => t.PostgresTableName).ToList();
        names.Should().NotContain("FailedLoginAttempts");
        names.Should().NotContain("EmailDeliveryEvents");
    }

    [Fact]
    public void RlsTables_has_exactly_25_entries()
    {
        UserOwnedModel.RlsTables(Ctx().Model).Should().HaveCount(25);
    }

    [Fact]
    public void FinanceTables_excludes_auth_internal_tables()
    {
        var names = UserOwnedModel.FinanceTables(Ctx().Model).Select(t => t.PostgresTableName).ToList();
        names.Should().NotContain(new[] { "UserSessions", "AuditLogs", "EmailConfirmationTokens" });
        names.Should().Contain(new[] { "Accounts", "TransactionAttachments" });
    }
}
