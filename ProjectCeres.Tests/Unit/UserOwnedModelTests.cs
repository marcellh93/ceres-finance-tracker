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
    public void RlsTables_includes_every_attachment_table()
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
    // 25 -> 26: SupportTickets (Stage 12.5). 26 -> 27: SupportTicketAttachments (Stage
    // 12.5). 27 -> 28: SupportMessages (Stage 12.6) — the conversation model split the
    // ticket's single Message column out into its own IUserOwned table.
    [Fact]
    public void RlsTables_has_exactly_28_entries()
    {
        UserOwnedModel.RlsTables(Ctx().Model).Should().HaveCount(28);
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
        // finance subset must equal the hand-list it replaced, or the dev sentinel remap
        // would silently start/stop touching a table.
        //
        // 16 -> 17: SupportTicketAttachments (Stage 12.5). It briefly sat in
        // AuthInternalTables, which kept this test green — and that was the bug. The set
        // is not "tables the remap skips"; it is auth machinery. An attachment holding a
        // user's financial screenshots belongs with its two sibling attachment tables in
        // the finance set that GDPR erasure, export and quota sweeps iterate. Excluding
        // it would have hidden those files from every one of them.
        // 17 -> 18: SupportMessages (Stage 12.6). SupportTickets itself stays out (it is
        // in AuthInternalTables — created after the sentinel era, never holds seed rows),
        // but the conversation body that used to live on SupportTicket.Message now lives
        // here, and it is not auth machinery — it belongs with the finance-and-attachment
        // subset for the same reason SupportTicketAttachments does.
        var expected = new[]
        {
            "Accounts", "Budgets", "Categories", "CategoryBudgets", "ImportProfiles",
            "ImportStagedTransactions", "ImportStagedTransfers", "ImportTransferExclusions",
            "LiabilityPayments", "RecurringTransactions", "SavedReports", "Settings",
            "SupportMessages", "SupportTicketAttachments",
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

    // Widening this is silent (narrowing would fail loudly on existing rows), and
    // there is no service-layer validator behind it yet, so the column width is
    // the only bound on user-submitted text. The conversation body moved to
    // SupportMessage.Body (Stage 12.6) and is asserted unbounded there — it is
    // IsRequired() but carries no HasMaxLength, unlike Subject.
    [Fact]
    public void SupportTicket_subject_stays_bounded()
    {
        var entity = Ctx().Model.FindEntityType(typeof(SupportTicket))!;

        entity.FindProperty(nameof(SupportTicket.Subject))!.GetMaxLength().Should().Be(200);
    }

    // Status is stored via HasConversion<int>(), so the ENUM ORDINAL is the real
    // database contract, not the C# name. Inserting a value at the top of the enum
    // — exactly what someone adding a "Pending" state would do — silently changes
    // the meaning of every stored row. Nothing else catches that.
    //
    // Stage 12.6 widened the enum from 4 values to 5 (Open, InProgress, Resolved,
    // Closed -> Open, Pending, OnHold, Solved, Closed) as part of the conversation
    // model: a ticket now has a real back-and-forth, so "waiting on the user" and
    // "blocked on something other than the user" needed to be distinguishable states.
    [Fact]
    public void SupportTicket_status_ordinals_are_the_stored_contract()
    {
        ((int)SupportTicketStatus.Open).Should().Be(0);
        ((int)SupportTicketStatus.Pending).Should().Be(1);
        ((int)SupportTicketStatus.OnHold).Should().Be(2);
        ((int)SupportTicketStatus.Solved).Should().Be(3);
        ((int)SupportTicketStatus.Closed).Should().Be(4);

        ((int)SupportTicketPriority.Low).Should().Be(0);
        ((int)SupportTicketPriority.Normal).Should().Be(1);
        ((int)SupportTicketPriority.High).Should().Be(2);
        ((int)SupportTicketPriority.Urgent).Should().Be(3);
    }

    // Cascade here, unlike the ticket self-FK: an attachment has no meaning without its
    // message, and an orphaned row would point at a file nothing can reach. The two
    // behaviours are deliberately opposite, so both are pinned — a future reader
    // "harmonising" them would silently break one or the other.
    //
    // Stage 12.6 moved the attachment's parent from SupportTicket to SupportMessage: a
    // screenshot now belongs to the specific message it was attached to, not the ticket
    // as a whole.
    [Fact]
    public void SupportTicketAttachment_cascades_from_its_message()
    {
        var entity = Ctx().Model.FindEntityType(typeof(SupportTicketAttachment))!;

        var parentFk = entity.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(SupportMessage));

        parentFk.DeleteBehavior.Should().Be(DeleteBehavior.Cascade,
            "an attachment cannot outlive the message it belongs to — the stored file would be unreachable");
    }

    // The security review's Critical finding, pinned — now against SupportMessage rather
    // than SupportTicket after the Stage 12.6 reshape. With a single-column FK, user B
    // could attach to user A's message (the RLS WITH CHECK pins UserId to the WRITER and
    // says nothing about the parent's owner, and Postgres runs FK checks through an RI
    // trigger that RLS does not apply to). A deleting their own message then destroyed
    // B's row via the cascade. Both were reproduced against the real database before the
    // composite key was added, and the same insert now fails with an FK violation.
    //
    // Referencing (Id, UserId) is what makes the divergence unrepresentable. A future
    // change back to a single-column FK reopens a cross-tenant destructive write.
    [Fact]
    public void SupportTicketAttachment_fk_is_scoped_to_the_message_owner()
    {
        var entity = Ctx().Model.FindEntityType(typeof(SupportTicketAttachment))!;

        var parentFk = entity.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(SupportMessage));

        parentFk.Properties.Select(p => p.Name).Should().BeEquivalentTo(
            new[] { nameof(SupportTicketAttachment.SupportMessageId), nameof(SupportTicketAttachment.UserId) },
            "a single-column FK lets an attachment hang off another user's message, which the " +
            "RLS-bypassing cascade then destroys");

        parentFk.PrincipalKey.Properties.Select(p => p.Name).Should().BeEquivalentTo(
            new[] { nameof(SupportMessage.Id), nameof(SupportMessage.UserId) });
    }

    [Fact]
    public void SupportMessage_exposes_the_alternate_key_the_attachment_fk_needs()
    {
        var entity = Ctx().Model.FindEntityType(typeof(SupportMessage))!;

        entity.GetKeys().Should().Contain(
            k => k.Properties.Count == 2
                 && k.Properties.Any(p => p.Name == nameof(SupportMessage.Id))
                 && k.Properties.Any(p => p.Name == nameof(SupportMessage.UserId)),
            "dropping this alternate key would force the attachment FK back to a single column");
    }

    [Fact]
    public void SupportTicket_exposes_the_alternate_key_the_message_fk_needs()
    {
        var entity = Ctx().Model.FindEntityType(typeof(SupportTicket))!;

        entity.GetKeys().Should().Contain(
            k => k.Properties.Count == 2
                 && k.Properties.Any(p => p.Name == nameof(SupportTicket.Id))
                 && k.Properties.Any(p => p.Name == nameof(SupportTicket.UserId)),
            "dropping this alternate key would force the message->ticket FK back to a single column");
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
