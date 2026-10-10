using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tools;

namespace ProjectCeres.Tests.Integration.Gdpr;

/// <summary>
/// Stage 13.b ship-gate: InactivitySweep.ProcessAsync against the real test database.
/// Each test seeds its own throwaway @inactivity-test.invalid users and asserts only on
/// those ids (the DB is shared). Clock is a fixed TimeProvider pinned to real "now" so
/// unrelated users (LastLoginAt defaulted to insert time) are never inside a horizon.
/// </summary>
[Collection("TestDbFixtureTests")]
public class InactivitySweepTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private readonly List<Guid> _userIds = [];
    private readonly DateTime _now = DateTime.UtcNow;
    private string _contentRoot = null!;
    private TokenLookupHasher _lookup = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _contentRoot = Path.Combine(Path.GetTempPath(), $"ceres-inactivity-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        _lookup = new TokenLookupHasher(Options.Create(new TokenLookupOptions
        {
            Secret = Convert.ToBase64String(new byte[32]),
        }));
    }

    public async Task DisposeAsync()
    {
        await using (var admin = _fixture.CreateAdminContext())
        {
            await UserOwnedCleanup.PurgeUsersAsync(admin, _userIds);
            await admin.Users.IgnoreQueryFilters().Where(u => _userIds.Contains(u.Id)).ExecuteDeleteAsync();
        }
        await _fixture.DisposeAsync();
        if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    private async Task<Guid> SeedUserAsync(
        TimeSpan inactiveFor, DateTime? sealedAt = null, DateTime? erasedAt = null)
    {
        var id = Guid.NewGuid();
        var email = $"sweep-{id:N}@inactivity-test.invalid";
        await using var admin = _fixture.CreateAdminContext();
        admin.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            LastLoginAt = _now - inactiveFor,
            SealedAt = sealedAt,
            ErasedAt = erasedAt,
        });
        await admin.SaveChangesAsync();
        _userIds.Add(id);
        return id;
    }

    private async Task<(int Warned, int Anonymised)> RunSweepAsync(Mock<IInactivityWarningNotificationService> warner)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_contentRoot);
        var clock = new FixedTimeProvider(_now);
        await using var db = _fixture.CreateAdminContext();
        return await InactivitySweep.ProcessAsync(
            db,
            new UserAnonymiser(db, env.Object, clock),
            warner.Object,
            new ErasurePseudonym(_lookup),
            clock,
            NullLogger.Instance,
            CancellationToken.None);
    }

    private static Mock<IInactivityWarningNotificationService> WarnerReturning(bool result)
    {
        var warner = new Mock<IInactivityWarningNotificationService>();
        warner.Setup(w => w.NotifyAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return warner;
    }

    private async Task<ApplicationUser> ReadUserAsync(Guid id)
    {
        await using var admin = _fixture.CreateAdminContext();
        return await admin.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == id);
    }

    private async Task<int> CountAuditAsync(Guid id, AuditLogAction action)
    {
        await using var admin = _fixture.CreateAdminContext();
        return await admin.AuditLogs.IgnoreQueryFilters()
            .CountAsync(a => a.UserId == id && a.Action == action);
    }

    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    [Fact]
    public async Task Warns_user_just_past_12_months_not_one_just_inside()
    {
        var past = await SeedUserAsync(InactivitySweep.WarningHorizon + Day);
        var inside = await SeedUserAsync(InactivitySweep.WarningHorizon - Day);
        var warner = WarnerReturning(true);

        await RunSweepAsync(warner);

        warner.Verify(w => w.NotifyAsync(past, 18, It.IsAny<CancellationToken>()), Times.Once);
        warner.Verify(w => w.NotifyAsync(inside, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        (await ReadUserAsync(past)).InactivityWarningSentAt.Should().NotBeNull();
        (await ReadUserAsync(inside)).InactivityWarningSentAt.Should().BeNull();
        (await CountAuditAsync(past, AuditLogAction.InactivityWarningSent)).Should().Be(1);
        (await CountAuditAsync(inside, AuditLogAction.InactivityWarningSent)).Should().Be(0);
    }

    [Fact]
    public async Task Anonymises_user_just_past_18_months_not_one_just_inside()
    {
        var past = await SeedUserAsync(InactivitySweep.AnonymiseHorizon + Day);
        var inside = await SeedUserAsync(InactivitySweep.AnonymiseHorizon - Day);

        await RunSweepAsync(WarnerReturning(true));

        var pastRow = await ReadUserAsync(past);
        pastRow.ErasedAt.Should().NotBeNull();
        pastRow.Email.Should().EndWith("@erased.invalid");
        var insideRow = await ReadUserAsync(inside);
        insideRow.ErasedAt.Should().BeNull();
        insideRow.Email.Should().EndWith("@inactivity-test.invalid");
        (await CountAuditAsync(past, AuditLogAction.InactivityAnonymised)).Should().Be(1);
        (await CountAuditAsync(inside, AuditLogAction.InactivityAnonymised)).Should().Be(0);
    }

    [Fact]
    public async Task Second_sweep_does_not_re_send_warning()
    {
        var user = await SeedUserAsync(InactivitySweep.WarningHorizon + Day);
        var warner = WarnerReturning(true);

        await RunSweepAsync(warner);
        var stamped = (await ReadUserAsync(user)).InactivityWarningSentAt;
        await RunSweepAsync(warner);

        warner.Verify(w => w.NotifyAsync(user, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        stamped.Should().NotBeNull();
        (await ReadUserAsync(user)).InactivityWarningSentAt.Should().Be(stamped);
        (await CountAuditAsync(user, AuditLogAction.InactivityWarningSent)).Should().Be(1);
    }

    [Fact]
    public async Task Skips_sealed_or_erased_accounts()
    {
        var sealedWarn = await SeedUserAsync(InactivitySweep.WarningHorizon + Day, sealedAt: _now.AddHours(-1));
        var erasedWarn = await SeedUserAsync(InactivitySweep.WarningHorizon + Day, erasedAt: _now.AddHours(-1));
        var sealedAnon = await SeedUserAsync(InactivitySweep.AnonymiseHorizon + Day, sealedAt: _now.AddHours(-1));
        var erasedAnon = await SeedUserAsync(InactivitySweep.AnonymiseHorizon + Day, erasedAt: _now.AddHours(-1));
        var warner = WarnerReturning(true);

        await RunSweepAsync(warner);

        foreach (var id in new[] { sealedWarn, erasedWarn, sealedAnon, erasedAnon })
        {
            warner.Verify(w => w.NotifyAsync(id, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            (await ReadUserAsync(id)).InactivityWarningSentAt.Should().BeNull();
            (await CountAuditAsync(id, AuditLogAction.InactivityWarningSent)).Should().Be(0);
            (await CountAuditAsync(id, AuditLogAction.InactivityAnonymised)).Should().Be(0);
        }
        var sealedRow = await ReadUserAsync(sealedAnon);
        sealedRow.ErasedAt.Should().BeNull();
        sealedRow.Email.Should().EndWith("@inactivity-test.invalid");
    }

    [Fact]
    public async Task Inactivity_anonymise_writes_no_GdprErasureCompleted_and_no_hold()
    {
        var user = await SeedUserAsync(InactivitySweep.AnonymiseHorizon + Day);
        var accountId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Accounts.Add(new Account
            {
                Id = accountId, Name = "Victim Checking", Description = "private note",
                AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = user,
            });
            admin.Categories.Add(new Category
            {
                Id = categoryId, Name = "Victim Groceries", CategoryTypeId = 2, IsActive = true, UserId = user,
            });
            admin.Transactions.Add(new Transaction
            {
                Id = txId, Date = DateOnly.FromDateTime(DateTime.Today), Amount = 12.34m,
                Description = "Paid by victim", AccountId = accountId, CategoryId = categoryId,
                CreatedAt = DateTime.UtcNow, UserId = user,
            });
            await admin.SaveChangesAsync();
        }

        await RunSweepAsync(WarnerReturning(true));

        (await CountAuditAsync(user, AuditLogAction.InactivityAnonymised)).Should().Be(1);
        (await CountAuditAsync(user, AuditLogAction.GdprErasureCompleted)).Should().Be(0);
        await using var check = _fixture.CreateAdminContext();
        (await check.ErasedEmailHolds.IgnoreQueryFilters().CountAsync(h => h.UserId == user)).Should().Be(0);
        (await ReadUserAsync(user)).ErasedAt.Should().NotBeNull();

        var tx = await check.Transactions.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == txId);
        tx.Description.Should().BeNull();
        tx.Amount.Should().Be(12.34m);
        var account = await check.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == accountId);
        account.Name.Should().StartWith("Erased ");
        account.Description.Should().BeNull();
    }

    [Fact]
    public async Task Failed_send_does_not_stamp_warning_sent()
    {
        var user = await SeedUserAsync(InactivitySweep.WarningHorizon + Day);
        var warner = WarnerReturning(false);

        await RunSweepAsync(warner);

        warner.Verify(w => w.NotifyAsync(user, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        (await ReadUserAsync(user)).InactivityWarningSentAt.Should().BeNull();
        (await CountAuditAsync(user, AuditLogAction.InactivityWarningSent)).Should().Be(0);

        // Still eligible: a later successful run warns them.
        var retry = WarnerReturning(true);
        await RunSweepAsync(retry);
        retry.Verify(w => w.NotifyAsync(user, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        (await ReadUserAsync(user)).InactivityWarningSentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Second_anonymise_pass_is_a_no_op()
    {
        var user = await SeedUserAsync(InactivitySweep.AnonymiseHorizon + Day);

        await RunSweepAsync(WarnerReturning(true));
        var first = await ReadUserAsync(user);
        var act = async () => await RunSweepAsync(WarnerReturning(true));

        await act.Should().NotThrowAsync();
        (await CountAuditAsync(user, AuditLogAction.InactivityAnonymised)).Should().Be(1);
        var second = await ReadUserAsync(user);
        second.Email.Should().Be(first.Email);
        second.ErasedAt.Should().Be(first.ErasedAt);
    }
}
