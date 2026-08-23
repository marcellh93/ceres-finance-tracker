using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Unit;

public class UserOwnedModelTests
{
    // Building DbContextOptions and reading .Model only triggers OnModelCreating —
    // it does not open a connection, so a placeholder Npgsql connection string is fine.
    private static AppDbContext Ctx() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options,
        new Common.FakeCurrentUserAccessor(Guid.Empty));

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

    // A deliberate count pin: adding a user-owned entity must fail this test so the
    // author consciously confirms the new table reached every registry (RLS policy
    // migration, query filter, cleanup) rather than only the DbSet.
    // 25 -> 26: SupportTickets (Stage 12.5). 26 -> 27: SupportTicketAttachments (Stage 12.5).
    [Fact]
    public void RlsTables_has_exactly_27_entries()
    {
        UserOwnedModel.RlsTables(Ctx().Model).Should().HaveCount(27);
    }

    [Fact]
    public void FinanceTables_excludes_auth_internal_tables()
    {
        var names = UserOwnedModel.FinanceTables(Ctx().Model).Select(t => t.PostgresTableName).ToList();
        names.Should().NotContain(new[] { "UserSessions", "AuditLogs", "EmailConfirmationTokens" });
        names.Should().Contain(new[] { "Accounts", "TransactionAttachments" });
    }

    [Fact]
    public void FinanceTables_matches_the_dev_seed_remap_set_exactly()
    {
        // Pins behaviour-preservation for SeedDevUser (Stage 9.5b Task 3): the model-derived
        // finance subset must equal the 16-table hand-list it replaced, or the dev sentinel
        // remap would silently start/stop touching a table.
        var expected = new[]
        {
            "Accounts", "Budgets", "Categories", "CategoryBudgets", "ImportProfiles",
            "ImportStagedTransactions", "ImportStagedTransfers", "ImportTransferExclusions",
            "LiabilityPayments", "RecurringTransactions", "SavedReports", "Settings",
            "TransactionAttachments", "Transactions", "TransferAttachments", "Transfers",
        };
        var names = UserOwnedModel.FinanceTables(Ctx().Model).Select(t => t.PostgresTableName);
        names.Should().BeEquivalentTo(expected);
    }
    // ── SupportTicket model shape ────────────────────────────────────────────
    //
    // No test in this repo asserted EF model SHAPE before these — only model
    // membership. SupportTicket is where that gap first has teeth: it carries the
    // project's first self-referential FK, and the delete behaviour is
    // load-bearing rather than incidental.

    // Restrict, not Cascade. A follow-up ticket is its own record of what was
    // reported; deleting an earlier ticket must never silently take the chain with
    // it. Flipping to Cascade is a two-word edit that regenerates the migration
    // cleanly and passes MigrationDriftTests, because model and snapshot still
    // agree — the first symptom would be a support thread vanishing once an admin
    // delete path exists. This is the negative assertion that guards it.
    [Fact]
    public void SupportTicket_self_reference_restricts_deletes_rather_than_cascading()
    {
        var entity = Ctx().Model.FindEntityType(typeof(SupportTicket))!;

        var selfFk = entity.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(SupportTicket));

        selfFk.DeleteBehavior.Should().Be(DeleteBehavior.Restrict,
            "deleting a ticket must never cascade to the follow-ups that reference it — " +
            "each is an independent record of what was reported");
    }

    // Widening these is silent (narrowing would fail loudly on existing rows), and
    // there is no service-layer validator behind them yet, so the column width is
    // the only bound on user-submitted text.
    [Fact]
    public void SupportTicket_free_text_columns_stay_bounded()
    {
        var entity = Ctx().Model.FindEntityType(typeof(SupportTicket))!;

        entity.FindProperty(nameof(SupportTicket.Subject))!.GetMaxLength().Should().Be(200);
        entity.FindProperty(nameof(SupportTicket.Message))!.GetMaxLength().Should().Be(5000);
    }

    // Status is stored via HasConversion<int>(), so the ENUM ORDINAL is the real
    // database contract, not the C# name. Inserting a value at the top of the enum
    // — exactly what someone adding a "Pending" state would do — silently changes
    // the meaning of every stored row. Nothing else catches that.
    [Fact]
    public void SupportTicket_status_ordinals_are_the_stored_contract()
    {
        ((int)SupportTicketStatus.Open).Should().Be(0);
        ((int)SupportTicketStatus.InProgress).Should().Be(1);
        ((int)SupportTicketStatus.Resolved).Should().Be(2);
        ((int)SupportTicketStatus.Closed).Should().Be(3);

        ((int)SupportTicketPriority.Low).Should().Be(0);
        ((int)SupportTicketPriority.Normal).Should().Be(1);
        ((int)SupportTicketPriority.High).Should().Be(2);
        ((int)SupportTicketPriority.Urgent).Should().Be(3);
    }

    // Cascade here, unlike the ticket self-FK: an attachment has no meaning without its
    // ticket, and an orphaned row would point at a file nothing can reach. The two
    // behaviours are deliberately opposite, so both are pinned — a future reader
    // "harmonising" them would silently break one or the other.
    [Fact]
    public void SupportTicketAttachment_cascades_from_its_ticket()
    {
        var entity = Ctx().Model.FindEntityType(typeof(SupportTicketAttachment))!;

        var parentFk = entity.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(SupportTicket));

        parentFk.DeleteBehavior.Should().Be(DeleteBehavior.Cascade,
            "an attachment cannot outlive the ticket it belongs to — the stored file would be unreachable");
    }

    [Fact]
    public void SupportTicketAttachment_path_columns_stay_bounded()
    {
        var entity = Ctx().Model.FindEntityType(typeof(SupportTicketAttachment))!;

        entity.FindProperty(nameof(SupportTicketAttachment.FileName))!.GetMaxLength().Should().Be(255);
        entity.FindProperty(nameof(SupportTicketAttachment.StoredPath))!.GetMaxLength().Should().Be(500);
        entity.FindProperty(nameof(SupportTicketAttachment.ContentType))!.GetMaxLength().Should().Be(100);
    }

    [Fact]
    public void SupportTicket_defaults_to_an_open_normal_priority_ticket()
    {
        var ticket = new SupportTicket();

        ticket.Status.Should().Be(SupportTicketStatus.Open);
        ticket.Priority.Should().Be(SupportTicketPriority.Normal);
        ticket.PrecedingTicketId.Should().BeNull("a standalone ticket continues nothing");
    }
}
