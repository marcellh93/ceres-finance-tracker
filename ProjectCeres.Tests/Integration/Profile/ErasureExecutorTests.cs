using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Profile;

/// <summary>
/// ErasureExecutor against the real project_ceres_test database, reading/writing
/// through AdminDbContext (BYPASSRLS) the same way the future ErasureWorker cron
/// will — a fresh throwaway user seeded directly via the admin connection.
///
/// Never the shared sentinel (<see cref="TestDbFixture.SentinelUserId"/>): this
/// suite hard-deletes AspNetUsers rows and purge-lane data, and the sentinel's
/// fixture Accounts/Categories are migration-seeded and not restorable.
/// </summary>
[Collection("TestDbFixtureTests")]
public class ErasureExecutorTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private readonly Guid _userId = Guid.NewGuid();
    private string _contentRoot = null!;
    private ErasurePseudonym _pseudonym = null!;
    private ErasureExecutor _executor = null!;

    private Guid _accountId;
    private Guid _categoryId;
    private Guid _transactionId;
    private Guid _budgetId;
    private Guid _recurringTransactionId;
    private Guid _supportTicketId;
    private Guid _supportMessageId;
    private Guid _supportAttachmentId;
    private Guid _transactionAttachmentId;
    private Guid _exportJobId;
    private Guid _priorLoginAuditId;
    private Guid _priorRegisteredAuditId;

    private string _supportAttachmentFullPath = null!;
    private string _transactionAttachmentFullPath = null!;
    private string _exportZipFullPath = null!;

    private const string RealEmail = "victim-erasure-test@erasure-test.invalid";
    private const string RealUserName = "victim-erasure-test@erasure-test.invalid";
    private const string RealAccountName = "Victim's Real Checking Account";
    private const string RealCategoryName = "Victim's Custom Category";

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        _contentRoot = Path.Combine(Path.GetTempPath(), $"ceres-erasure-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);

        var lookup = new TokenLookupHasher(Options.Create(new TokenLookupOptions
        {
            Secret = Convert.ToBase64String(new byte[32]),
        }));
        _pseudonym = new ErasurePseudonym(lookup);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Users.Add(new ApplicationUser
            {
                Id = _userId,
                UserName = RealUserName,
                NormalizedUserName = RealUserName.ToUpperInvariant(),
                Email = RealEmail,
                NormalizedEmail = RealEmail.ToUpperInvariant(),
                PhoneNumber = "+34600000000",
                EmailConfirmed = true,
            });

            admin.ErasureRequests.Add(new ErasureRequest
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                Status = ErasureStatus.Sealed,
                RequestedAt = DateTime.UtcNow,
                ExecuteAfter = DateTime.UtcNow.AddHours(72),
                CancelTokenLookup = new byte[32],
                CancelTokenHash = "unused",
            });

            _accountId = Guid.NewGuid();
            admin.Accounts.Add(new Account
            {
                Id = _accountId,
                Name = RealAccountName,
                Description = "Personal notes mentioning " + RealEmail,
                AccountTypeId = 1,
                CurrencyId = 1,
                IsActive = true,
                UserId = _userId,
            });

            _categoryId = Guid.NewGuid();
            admin.Categories.Add(new Category
            {
                Id = _categoryId,
                Name = RealCategoryName,
                CategoryTypeId = 2,
                IsActive = true,
                UserId = _userId,
            });

            _budgetId = Guid.NewGuid();
            admin.Budgets.Add(new Budget
            {
                Id = _budgetId,
                Name = "Victim's Vacation Fund",
                TargetAmount = 1000m,
                CurrencyId = 1,
                StartDate = new DateOnly(2026, 1, 1),
                GoalType = "Spending",
                IsActive = true,
                UserId = _userId,
            });

            admin.Settings.Add(new Settings
            {
                UserId = _userId,
                NumberFormat = "en-US",
                DateFormat = "MM/dd/yyyy",
                DefaultCurrencyId = 1,
            });

            _recurringTransactionId = Guid.NewGuid();
            admin.RecurringTransactions.Add(new RecurringTransaction
            {
                Id = _recurringTransactionId,
                Name = "Victim's Rent",
                AccountId = _accountId,
                CategoryId = _categoryId,
                Frequency = Frequency.Monthly,
                NextDueDate = DateOnly.FromDateTime(DateTime.Today),
                IsActive = true,
                UserId = _userId,
            });

            _transactionId = Guid.NewGuid();
            admin.Transactions.Add(new Transaction
            {
                Id = _transactionId,
                Date = DateOnly.FromDateTime(DateTime.Today),
                Amount = 42.50m,
                Description = "Paid by " + RealEmail,
                AccountId = _accountId,
                CategoryId = _categoryId,
                // Deliberately links to the purge-lane Budget: Transaction (statutory,
                // retained) → Budget (purge, deleted) is DeleteBehavior.Restrict —
                // pins that the executor severs this FK before deleting the Budget.
                BudgetId = _budgetId,
                CreatedAt = DateTime.UtcNow,
                UserId = _userId,
            });

            // Statutory attachment: physical file must SURVIVE erasure.
            _transactionAttachmentId = Guid.NewGuid();
            var txAttachRelPath = Path.Combine("uploads", _transactionId.ToString(), "receipt.jpg");
            _transactionAttachmentFullPath = Path.Combine(_contentRoot, txAttachRelPath);
            Directory.CreateDirectory(Path.GetDirectoryName(_transactionAttachmentFullPath)!);
            await File.WriteAllBytesAsync(_transactionAttachmentFullPath, [0xFF, 0xD8, 0xFF, 0xD9]);
            admin.TransactionAttachments.Add(new TransactionAttachment
            {
                Id = _transactionAttachmentId,
                TransactionId = _transactionId,
                UserId = _userId,
                FileName = "real-receipt-name.jpg",
                StoredPath = txAttachRelPath,
                ContentType = "image/jpeg",
                FileSizeBytes = 4,
                UploadedAt = DateTime.UtcNow,
            });

            _supportTicketId = Guid.NewGuid();
            admin.SupportTickets.Add(new SupportTicket
            {
                Id = _supportTicketId,
                UserId = _userId,
                Subject = $"Can't log in as {RealEmail}",
                Status = SupportTicketStatus.Open,
                Priority = SupportTicketPriority.Normal,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });

            _supportMessageId = Guid.NewGuid();
            admin.SupportMessages.Add(new SupportMessage
            {
                Id = _supportMessageId,
                UserId = _userId,
                SupportTicketId = _supportTicketId,
                AuthorRole = SupportMessageAuthor.User,
                Body = $"Please contact me at {RealEmail} about this issue.",
                CreatedAt = DateTime.UtcNow,
            });

            // Support attachment: physical file must be DELETED by erasure.
            _supportAttachmentId = Guid.NewGuid();
            var supportAttachRelPath = Path.Combine("uploads", "support", _supportMessageId.ToString(), "screenshot.png");
            _supportAttachmentFullPath = Path.Combine(_contentRoot, supportAttachRelPath);
            Directory.CreateDirectory(Path.GetDirectoryName(_supportAttachmentFullPath)!);
            await File.WriteAllBytesAsync(_supportAttachmentFullPath, [0x89, 0x50, 0x4E, 0x47]);
            admin.SupportTicketAttachments.Add(new SupportTicketAttachment
            {
                Id = _supportAttachmentId,
                SupportMessageId = _supportMessageId,
                UserId = _userId,
                FileName = "real-screenshot-name.png",
                StoredPath = supportAttachRelPath,
                ContentType = "image/png",
                FileSizeBytes = 4,
                UploadedAt = DateTime.UtcNow,
            });

            _exportJobId = Guid.NewGuid();
            var exportRelPath = $"export-{_userId:N}.zip";
            _exportZipFullPath = Path.Combine(_contentRoot, "exports", exportRelPath);
            Directory.CreateDirectory(Path.GetDirectoryName(_exportZipFullPath)!);
            await File.WriteAllBytesAsync(_exportZipFullPath, [0x50, 0x4B, 0x03, 0x04]);
            admin.ExportJobs.Add(new ExportJob
            {
                Id = _exportJobId,
                UserId = _userId,
                Status = ExportJobStatus.Ready,
                Format = ExportFormat.Zip,
                RequestedAt = DateTime.UtcNow,
                ReadyAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                StoredPath = Path.Combine("exports", exportRelPath),
                TokenLookup = new byte[32],
                TokenHash = "unused",
            });

            // Prior audit rows (Stage 6.14 GDPR-on-erasure): historical IpAddress values
            // must be rewritten to "erased" by the executor, separate from the one new
            // completion row.
            _priorRegisteredAuditId = Guid.NewGuid();
            admin.AuditLogs.Add(new AuditLog
            {
                Id = _priorRegisteredAuditId,
                UserId = _userId,
                Action = AuditLogAction.Registered,
                OccurredAt = DateTime.UtcNow.AddDays(-10),
                IpAddress = "203.0.113.5",
            });
            _priorLoginAuditId = Guid.NewGuid();
            admin.AuditLogs.Add(new AuditLog
            {
                Id = _priorLoginAuditId,
                UserId = _userId,
                Action = AuditLogAction.LoginSucceeded,
                OccurredAt = DateTime.UtcNow.AddDays(-1),
                IpAddress = "203.0.113.5",
            });

            await admin.SaveChangesAsync();
        }

        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_contentRoot);

        _executor = new ErasureExecutor(
            _fixture.CreateAdminContext(), _pseudonym, env.Object, TimeProvider.System,
            new LowercaseLookupNormalizer(), lookup,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ErasureExecutor>.Instance);
    }

    public async Task DisposeAsync()
    {
        await using (var admin = _fixture.CreateAdminContext())
        {
            await admin.ErasureRequests.IgnoreQueryFilters().Where(r => r.UserId == _userId).ExecuteDeleteAsync();
            await UserOwnedCleanup.PurgeUserAsync(admin, _userId);
            await admin.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
        }

        await _fixture.DisposeAsync();

        if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
    }

    [Fact]
    public async Task ExecuteAsync_runs_the_full_eight_sequence()
    {
        await _executor.ExecuteAsync(_userId, CancellationToken.None);

        await using var admin = _fixture.CreateAdminContext();

        // Purge lane: Budget, Settings, RecurringTransaction are gone entirely.
        (await admin.Budgets.IgnoreQueryFilters().CountAsync(b => b.Id == _budgetId))
            .Should().Be(0, "purge-lane rows are hard-deleted");
        (await admin.Settings.IgnoreQueryFilters().CountAsync(s => s.UserId == _userId))
            .Should().Be(0, "purge-lane rows are hard-deleted");
        (await admin.RecurringTransactions.IgnoreQueryFilters().CountAsync(r => r.Id == _recurringTransactionId))
            .Should().Be(0, "purge-lane rows are hard-deleted");

        // Statutory lane: rows remain, identity is anonymised (negative assertions).
        var account = await admin.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == _accountId);
        account.Name.Should().NotBe(RealAccountName);
        account.Name.Should().NotContain(RealEmail);
        account.Description.Should().BeNull("the seeded description contained the real email and must be cleared, not just fail to match a substring check that passes vacuously on null");

        var category = await admin.Categories.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == _categoryId);
        category.Name.Should().NotBe(RealCategoryName);

        var transaction = await admin.Transactions.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == _transactionId);
        transaction.Description.Should().BeNull("the seeded description contained the real email and must be cleared, not just fail to match a substring check that passes vacuously on null");
        transaction.AccountId.Should().Be(_accountId, "the statutory row survives with its FK intact");
        transaction.BudgetId.Should().BeNull("the purge-lane Budget it pointed to is gone; the FK must be severed first");

        // Statutory attachment: file stays on disk (evidentiary record); FileName anonymised.
        var txAttachment = await admin.TransactionAttachments.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(a => a.Id == _transactionAttachmentId);
        txAttachment.FileName.Should().NotBe("real-receipt-name.jpg");
        File.Exists(_transactionAttachmentFullPath).Should().BeTrue(
            "a statutory attachment's physical file is the legal record and must NOT be deleted");

        // Support lane: thread survives, body redacted, attachment row survives redacted,
        // attachment file deleted.
        var message = await admin.SupportMessages.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == _supportMessageId);
        message.Body.Should().NotContain(RealEmail, "the redacted body must not leak the real email");
        (await admin.SupportTickets.IgnoreQueryFilters().CountAsync(t => t.Id == _supportTicketId))
            .Should().Be(1, "the ticket survives as de-identified knowledge");

        var supportAttachment = await admin.SupportTicketAttachments.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(a => a.Id == _supportAttachmentId);
        supportAttachment.FileName.Should().Be("[removed]");
        File.Exists(_supportAttachmentFullPath).Should().BeFalse(
            "a support attachment has no legal retention duty and its file must be deleted");

        var ticket = await admin.SupportTickets.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == _supportTicketId);
        ticket.Subject.Should().NotContain(RealEmail, "a ticket subject is free text and can carry the same PII as a message body");

        // ExportJob: row + ZIP both gone.
        (await admin.ExportJobs.IgnoreQueryFilters().CountAsync(j => j.Id == _exportJobId))
            .Should().Be(0, "the outstanding export job row is deleted");
        File.Exists(_exportZipFullPath).Should().BeFalse("the outstanding export ZIP is deleted");

        // Account: ErasedAt set, identity anonymised.
        var user = await admin.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == _userId);
        user.ErasedAt.Should().NotBeNull();
        user.Email.Should().NotBe(RealEmail);
        user.UserName.Should().NotBe(RealUserName);
        user.NormalizedEmail.Should().NotContain(RealEmail.ToUpperInvariant());
        user.NormalizedUserName.Should().NotContain(RealUserName.ToUpperInvariant());
        user.NormalizedEmail.Should().Be(user.Email!.ToUpperInvariant(), "NormalizedEmail follows Identity's ToUpperInvariant convention");
        user.NormalizedUserName.Should().Be(user.UserName!.ToUpperInvariant());

        // Audit: exactly one completion row, pseudonymised, landed inside the real
        // transaction on AdminDbContext (post-Task-6-fixloop-round-2: this write is
        // no longer routed through IAuditLogWriter, so it's read back from the DB
        // directly rather than an in-memory fake).
        var expectedPseudonym = _pseudonym.Compute(_userId);
        var completions = await admin.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.UserId == _userId && a.Action == AuditLogAction.GdprErasureCompleted)
            .ToListAsync();
        completions.Should().ContainSingle();
        completions[0].UserId.Should().Be(_userId);
        completions[0].EntityType.Should().Be(expectedPseudonym);
        completions[0].EntityType.Should().NotBe(_userId.ToString());

        // ErasureRequest: Status flipped to Completed.
        var request = await admin.ErasureRequests.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.UserId == _userId);
        request.Status.Should().Be(ErasureStatus.Completed);
        request.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_on_a_completed_request_is_a_no_op()
    {
        await _executor.ExecuteAsync(_userId, CancellationToken.None);

        // A second call against an already-Completed request must not throw. This
        // does NOT exercise re-running the lane logic — the top-of-method query only
        // matches Status == Sealed, so this hits the early-return before any lane
        // runs. See ExecuteAsync_a_second_pass_after_a_simulated_crash_does_not_double_complete
        // for the genuine re-run-against-already-processed-data case.
        var act = async () => await _executor.ExecuteAsync(_userId, CancellationToken.None);
        await act.Should().NotThrowAsync();

        await using var check = _fixture.CreateAdminContext();
        (await check.AuditLogs.IgnoreQueryFilters()
            .CountAsync(a => a.UserId == _userId && a.Action == AuditLogAction.GdprErasureCompleted))
            .Should().Be(1, "a re-run after Completed must not write a second audit row");
    }

    [Fact]
    public async Task ExecuteAsync_a_second_pass_after_a_simulated_crash_does_not_double_complete()
    {
        await _executor.ExecuteAsync(_userId, CancellationToken.None);

        // Simulates an operator/retry re-running a fully-completed erasure: force
        // the request back to Sealed so the second call actually re-runs every lane
        // against already-processed data, rather than hitting the early-return.
        // Post-Task-6-fixloop-round-2, the completion row from the first real
        // ExecuteAsync call above already landed in AuditLogs for real (direct
        // db.AuditLogs write on the same AdminDbContext/transaction) — no manual
        // seed needed to simulate it, unlike when the write went through the fake
        // IAuditLogWriter and never touched the DB.
        await using (var admin = _fixture.CreateAdminContext())
        {
            await admin.ErasureRequests.IgnoreQueryFilters()
                .Where(r => r.UserId == _userId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ErasureStatus.Sealed));
        }

        var act = async () => await _executor.ExecuteAsync(_userId, CancellationToken.None);
        await act.Should().NotThrowAsync("a re-run against already-erased data must not throw");

        await using var check = _fixture.CreateAdminContext();
        (await check.AuditLogs.IgnoreQueryFilters()
            .CountAsync(a => a.UserId == _userId && a.Action == AuditLogAction.GdprErasureCompleted))
            .Should().Be(1, "a second real pass must not write a second completion audit row");

        var request = await check.ErasureRequests.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.UserId == _userId);
        request.Status.Should().Be(ErasureStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_rewrites_historical_audit_log_ip_addresses_to_erased()
    {
        await _executor.ExecuteAsync(_userId, CancellationToken.None);

        await using var admin = _fixture.CreateAdminContext();
        var rows = await admin.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Id == _priorRegisteredAuditId || a.Id == _priorLoginAuditId)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(a => a.IpAddress == "erased",
            "a user's prior audit rows must have their real IP addresses erased, not just the new completion row");
    }

    [Fact]
    public async Task ExecuteAsync_writes_an_ErasedEmailHold_with_a_real_thirty_day_expiry()
    {
        var before = TimeProvider.System.GetUtcNow().UtcDateTime;
        await _executor.ExecuteAsync(_userId, CancellationToken.None);
        var after = TimeProvider.System.GetUtcNow().UtcDateTime;

        await using var admin = _fixture.CreateAdminContext();
        var hold = await admin.ErasedEmailHolds.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(h => h.UserId == _userId);

        var expectedFingerprint = new TokenLookupHasher(Options.Create(new TokenLookupOptions
        {
            Secret = Convert.ToBase64String(new byte[32]),
        })).ComputeLookup(new LowercaseLookupNormalizer().NormalizeEmail(RealEmail)!);
        hold.EmailFingerprint.Should().BeEquivalentTo(expectedFingerprint,
            "the hold must fingerprint the REAL pre-anonymisation email via the project's ILookupNormalizer");

        hold.ErasedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        hold.ExpiresAt.Should().BeCloseTo(hold.ErasedAt.AddDays(30), TimeSpan.FromSeconds(5),
            "ExpiresAt must be the real 30-day arithmetic, not a hand-seeded value");
    }

    [Fact]
    public async Task ExecuteAsync_upserts_the_hold_when_the_same_email_was_erased_before()
    {
        // Simulates: erase user A with RealEmail, 30+ days pass, someone re-registers
        // RealEmail as a different user, that user is erased too. The second erasure
        // must update the existing fingerprint row, not collide on the unique index.
        var fingerprint = new TokenLookupHasher(Options.Create(new TokenLookupOptions
        {
            Secret = Convert.ToBase64String(new byte[32]),
        })).ComputeLookup(new LowercaseLookupNormalizer().NormalizeEmail(RealEmail)!);

        var priorHoldId = Guid.NewGuid();
        var priorOwnerId = Guid.NewGuid();
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.ErasedEmailHolds.Add(new ErasedEmailHold
            {
                Id = priorHoldId,
                UserId = priorOwnerId,
                EmailFingerprint = fingerprint,
                ErasedAt = DateTime.UtcNow.AddDays(-40),
                ExpiresAt = DateTime.UtcNow.AddDays(-10),
            });
            await admin.SaveChangesAsync();
        }

        var act = async () => await _executor.ExecuteAsync(_userId, CancellationToken.None);
        await act.Should().NotThrowAsync("a repeat erasure of the same email must upsert, not collide on the unique index");

        await using var check = _fixture.CreateAdminContext();
        var rows = await check.ErasedEmailHolds.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.EmailFingerprint == fingerprint)
            .ToListAsync();
        rows.Should().ContainSingle("the existing row must be updated in place, not duplicated");
        rows[0].Id.Should().Be(priorHoldId, "the upsert reuses the existing row's identity");
        rows[0].UserId.Should().Be(_userId, "the row now reflects the most recent erasure's owner");

        await using var cleanup = _fixture.CreateAdminContext();
        await cleanup.ErasedEmailHolds.IgnoreQueryFilters()
            .Where(h => h.Id == priorHoldId).ExecuteDeleteAsync();
    }
}
