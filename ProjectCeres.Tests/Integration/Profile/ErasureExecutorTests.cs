using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Profile;

/// <summary>Records the audit calls the executor makes.</summary>
sealed class RecordingErasureAuditLogWriter : IAuditLogWriter
{
    public List<(Guid UserId, AuditLogAction Action, string? EntityType, Guid? EntityId)> Recorded { get; } = [];
    public Task RecordAsync(Guid userId, AuditLogAction action, string? entityType = null,
        Guid? entityId = null, CancellationToken ct = default)
    {
        Recorded.Add((userId, action, entityType, entityId));
        return Task.CompletedTask;
    }
}

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
    private RecordingErasureAuditLogWriter _audit = null!;
    private ErasurePseudonym _pseudonym = null!;
    private ErasureExecutor _executor = null!;

    private Guid _accountId;
    private Guid _categoryId;
    private Guid _transactionId;
    private Guid _budgetId;
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
                Subject = "Help with my account",
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

        _audit = new RecordingErasureAuditLogWriter();
        _executor = new ErasureExecutor(
            _fixture.CreateAdminContext(), _pseudonym, _audit, env.Object, TimeProvider.System);
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

        // Purge lane: Budget is gone entirely.
        (await admin.Budgets.IgnoreQueryFilters().CountAsync(b => b.Id == _budgetId))
            .Should().Be(0, "purge-lane rows are hard-deleted");

        // Statutory lane: rows remain, identity is anonymised (negative assertions).
        var account = await admin.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == _accountId);
        account.Name.Should().NotBe(RealAccountName);
        account.Name.Should().NotContain(RealEmail);
        account.Description.Should().NotContain(RealEmail);

        var category = await admin.Categories.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == _categoryId);
        category.Name.Should().NotBe(RealCategoryName);

        var transaction = await admin.Transactions.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == _transactionId);
        transaction.Description.Should().NotContain(RealEmail);
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

        // Audit: exactly one completion call, pseudonymised. IAuditLogWriter is a
        // fake here (mirrors ExportJobServiceTests/ErasureServiceTests) — the real
        // AuditLogWriter opens its own DI scope + PreAuthUserScope, which this
        // fixture does not wire up; asserting the in-memory recording is the
        // established pattern for this DbContext-less dependency.
        var expectedPseudonym = _pseudonym.Compute(_userId);
        var completions = _audit.Recorded.Where(r => r.Action == AuditLogAction.GdprErasureCompleted).ToList();
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
    public async Task ExecuteAsync_is_idempotent_on_a_second_run()
    {
        await _executor.ExecuteAsync(_userId, CancellationToken.None);

        // A second run (simulating a crash-then-retry) must not throw, and must
        // remain a no-op: 0-row purge deletes and already-anonymised statutory
        // rows are legitimate, and the ErasureRequest is already Completed so the
        // executor finds no Sealed request and returns immediately.
        var act = async () => await _executor.ExecuteAsync(_userId, CancellationToken.None);
        await act.Should().NotThrowAsync();

        _audit.Recorded.Count(r => r.Action == AuditLogAction.GdprErasureCompleted)
            .Should().Be(1, "a re-run after Completed must not write a second audit row");
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
}
