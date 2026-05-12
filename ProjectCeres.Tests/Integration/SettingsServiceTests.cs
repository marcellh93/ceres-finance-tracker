using FluentAssertions;
using ProjectCeres.Common;
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
[Collection("IntegrationTests")]
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

}
