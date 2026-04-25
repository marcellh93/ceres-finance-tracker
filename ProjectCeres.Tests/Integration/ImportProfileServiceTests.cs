using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for CsvImportProfileService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
/// </summary>
[Collection("IntegrationTests")]
public class ImportProfileServiceTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private ImportProfileService _service = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _service = new ImportProfileService(_fixture.Db);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ImportColumnMappings ValidMappings() => new()
    {
        DateColumn        = "Date",
        AmountColumn      = "Amount",
        DescriptionColumn = "Description",
        CategoryColumn    = "Category"
    };

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateProfile_WithValidMappings_SucceedsAndMappingsRetrievable()
    {
        var mappings = ValidMappings();

        var id = await _service.CreateAsync("Bank A", ImportFormat.Csv, mappings);

        var retrieved = await _service.GetByIdAsync(id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Bank A");
        retrieved.Mappings.DateColumn.Should().Be("Date");
        retrieved.Mappings.AmountColumn.Should().Be("Amount");
        retrieved.Mappings.DescriptionColumn.Should().Be("Description");
        retrieved.Mappings.CategoryColumn.Should().Be("Category");
        retrieved.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task SoftDeleteProfile_SetsDeletedAt_ExcludedFromActiveList()
    {
        var id = await _service.CreateAsync("Bank B", ImportFormat.Csv, ValidMappings());

        await _service.DeleteAsync(id);

        var activeList = await _service.GetAllActiveAsync();
        activeList.Should().NotContain(p => p.Id == id);

        var deletedList = await _service.GetRecentlyDeletedAsync();
        deletedList.Should().Contain(p => p.Id == id);
    }

    [Fact]
    public async Task GetRecentlyDeletedAsync_ExcludesProfilesDeletedMoreThan90DaysAgo()
    {
        // Insert a profile with DeletedAt set to 91 days ago directly via DbContext.
        var old = new ImportProfile
        {
            Id             = Guid.NewGuid(),
            Name           = "Old Bank",
            ColumnMappings = "{}",
            CreatedAt      = DateTime.UtcNow.AddDays(-100),
            DeletedAt      = DateTime.UtcNow.AddDays(-91)
        };
        _fixture.Db.ImportProfiles.Add(old);
        await _fixture.Db.SaveChangesAsync();

        var deletedList = await _service.GetRecentlyDeletedAsync();

        deletedList.Should().NotContain(p => p.Id == old.Id);
    }

    [Fact]
    public async Task UpdateMappings_NewMappingsRetrievable()
    {
        var id = await _service.CreateAsync("Bank C", ImportFormat.Csv, ValidMappings());

        var updated = new ImportColumnMappings
        {
            DateColumn        = "Txn Date",
            AmountColumn      = "Debit",
            DescriptionColumn = "Memo",
            CategoryColumn    = null
        };
        await _service.UpdateAsync(id, "Bank C Renamed", updated);

        var retrieved = await _service.GetByIdAsync(id);
        retrieved!.Name.Should().Be("Bank C Renamed");
        retrieved.Mappings.DateColumn.Should().Be("Txn Date");
        retrieved.Mappings.AmountColumn.Should().Be("Debit");
        retrieved.Mappings.DescriptionColumn.Should().Be("Memo");
        retrieved.Mappings.CategoryColumn.Should().BeNull();
    }

    [Fact]
    public async Task RecoverDeletedProfile_WithinWindow_RestoredToActiveList()
    {
        var id = await _service.CreateAsync("Bank D", ImportFormat.Csv, ValidMappings());
        await _service.DeleteAsync(id);

        await _service.RecoverAsync(id);

        var activeList = await _service.GetAllActiveAsync();
        activeList.Should().Contain(p => p.Id == id);

        var deletedList = await _service.GetRecentlyDeletedAsync();
        deletedList.Should().NotContain(p => p.Id == id);
    }
}
