using FluentAssertions;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for CategoryService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   CategoryId 20000000-0000-0000-0000-000000000001 = Opening Balance (IsSystem=true, Income)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing / Rent  (IsSystem=false, Expense)
///   CategoryTypeId 1 = Income, CategoryTypeId 2 = Expense
/// </summary>
public class CategoryServiceTests : IAsyncLifetime
{
    private static readonly Guid SystemCategoryId    = new("20000000-0000-0000-0000-000000000001");
    private static readonly Guid NonSystemCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private CategoryService _service = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _service = new CategoryService(_fixture.Db);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_PersistsCategory()
    {
        var category = await _service.CreateAsync(new CategoryCreateViewModel
        {
            Name           = "Test Expense",
            CategoryTypeId = 2,   // Expense
            LifestyleTag   = "Needs"
        });

        var reloaded = await _fixture.Db.Categories.FindAsync(category.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Name.Should().Be("Test Expense");
        reloaded.IsSystem.Should().BeFalse();
        reloaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_SystemCategory_Throws()
    {
        var act = async () => await _service.UpdateAsync(new CategoryEditViewModel
        {
            Id           = SystemCategoryId,
            Name         = "Hacked Name",
            LifestyleTag = null
        });

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*System categories*");
    }

    [Fact]
    public async Task DeactivateAsync_SystemCategory_Throws()
    {
        var act = async () => await _service.DeactivateAsync(SystemCategoryId);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*System categories*");
    }

    [Fact]
    public async Task DeactivateAsync_NonSystemCategory_SetsIsActiveFalse()
    {
        // Use a seeded non-system category (Housing/Rent).
        await _service.DeactivateAsync(NonSystemCategoryId);

        var reloaded = await _fixture.Db.Categories.FindAsync(NonSystemCategoryId);
        reloaded!.IsActive.Should().BeFalse();
    }
}
