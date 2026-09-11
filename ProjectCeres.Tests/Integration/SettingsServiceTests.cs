using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Tests.Common;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for SettingsService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   CurrencyId 1 = EUR
///   CurrencyId 2 = USD
/// </summary>
[Collection("TestDbFixtureTests")]
public class SettingsServiceTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private SettingsService _service = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _service = new SettingsService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // EnsureExistsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EnsureExistsAsync_CreatesDefaultSettings_WhenNoneExist()
    {
        // The test DB is seeded with a settings row; delete it so we test creation from scratch.
        var existing = _fixture.Db.Settings.FirstOrDefault();
        if (existing is not null)
        {
            _fixture.Db.Settings.Remove(existing);
            await _fixture.Db.SaveChangesAsync();
        }

        await _service.EnsureExistsAsync();

        var settings = _fixture.Db.Settings.FirstOrDefault();
        settings.Should().NotBeNull();
        settings!.NumberFormat.Should().Be("comma_decimal");
        settings.DateFormat.Should().Be("DD/MM/YYYY");
        settings.DefaultCurrencyId.Should().Be(1);
    }

    [Fact]
    public async Task EnsureExistsAsync_DoesNotDuplicate_WhenSettingsAlreadyExist()
    {
        // Row already exists from seed — calling again must not create a second row.
        await _service.EnsureExistsAsync();

        var count = _fixture.Db.Settings.Count();
        count.Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // GetAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ReturnsSettings_WithDefaultCurrencyNavigationProperty()
    {
        var settings = await _service.GetAsync();

        settings.Should().NotBeNull();
        settings.DefaultCurrency.Should().NotBeNull();
        settings.DefaultCurrency.Code.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetAsync_CreatesDefaultRow_WhenNoneExist()
    {
        var existing = _fixture.Db.Settings.FirstOrDefault();
        if (existing is not null)
        {
            _fixture.Db.Settings.Remove(existing);
            await _fixture.Db.SaveChangesAsync();
        }

        var settings = await _service.GetAsync();

        settings.Should().NotBeNull();
        settings.NumberFormat.Should().Be("comma_decimal");
        settings.DefaultCurrencyId.Should().Be(1);
    }

    // Each request gets its own scoped AppDbContext. Concurrent first-touch GetAsync
    // by the same user issues two INSERTs that collide on UNIQUE(UserId): one must
    // win, the other must recover (re-fetch the winning row) and return it — never
    // surface DbUpdateException to the caller.
    //
    // This test deliberately bypasses the fixture's per-test transaction (which would
    // serialise the two INSERTs and mask the race). It cleans up its own row.
    [Fact]
    public async Task GetAsync_concurrent_first_touch_returns_same_row_both_callers()
    {
        var userId = Guid.NewGuid();
        var user   = new FakeCurrentUserAccessor(userId);

        await using var dbA = NonTransactionalContext(user);
        await using var dbB = NonTransactionalContext(user);
        await using var verify = NonTransactionalContext(user);

        var serviceA = new SettingsService(dbA, user);
        var serviceB = new SettingsService(dbB, user);

        try
        {
            var both = await Task.WhenAll(serviceA.GetAsync(), serviceB.GetAsync());
            var (a, b) = (both[0], both[1]);

            a.UserId.Should().Be(userId);
            b.UserId.Should().Be(userId);
            a.Id.Should().Be(b.Id);

            verify.Settings.Count(s => s.UserId == userId).Should().Be(1);
        }
        finally
        {
            await verify.Settings.Where(s => s.UserId == userId).ExecuteDeleteAsync();
        }
    }

    private static AppDbContext NonTransactionalContext(ICurrentUserAccessor user)
    {
        // Stage 7.6.7 / ADR-0073: interceptor takes UserContext via the accessor — no
        // tagger param. The settings tests bind a real user, so the interceptor sees
        // Resolved.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestDbFixture.AppConnectionString)
            .AddInterceptors(new UserOwnershipInterceptor(user))
            .AddInterceptors(new RowLevelSecurityInterceptor(
                user, Microsoft.Extensions.Logging.Abstractions.NullLogger<RowLevelSecurityInterceptor>.Instance))
            .Options;
        return new AppDbContext(options, user);
    }
}
