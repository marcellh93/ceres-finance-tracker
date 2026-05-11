using FluentAssertions;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Common;

public class CategoriesDefaultsTests
{
    [Fact]
    public void Defaults_includes_opening_balance_as_system()
    {
        Categories.Defaults.Should().ContainSingle(c => c.Name == "Opening Balance" && c.IsSystem);
    }

    [Fact]
    public void Defaults_includes_exactly_two_reserved_uncategorized_rows()
    {
        var reserved = Categories.Defaults.Where(c => c.IsReserved).ToList();
        reserved.Should().HaveCount(2);
        reserved.Should().Contain(c => c.Name == "Uncategorized Income");
        reserved.Should().Contain(c => c.Name == "Uncategorized Expense");
    }

    [Fact]
    public void Defaults_includes_all_26_canonical_categories()
    {
        Categories.Defaults.Should().HaveCount(26);
    }

    [Fact]
    public void Income_categories_have_CategoryTypeId_1()
    {
        var incomeNames = new[] { "Opening Balance", "Salary", "Freelance Income", "Rental Income", "Investment Income", "Business Income", "Other Income", "Uncategorized Income" };
        foreach (var name in incomeNames)
        {
            Categories.Defaults.Single(c => c.Name == name).CategoryTypeId.Should().Be(1, $"{name} is an Income category");
        }
    }

    [Fact]
    public void Expense_categories_have_CategoryTypeId_2()
    {
        // Spot-check three to confirm the type id mapping.
        Categories.Defaults.Single(c => c.Name == "Housing / Rent").CategoryTypeId.Should().Be(2);
        Categories.Defaults.Single(c => c.Name == "Groceries").CategoryTypeId.Should().Be(2);
        Categories.Defaults.Single(c => c.Name == "Uncategorized Expense").CategoryTypeId.Should().Be(2);
    }
}
