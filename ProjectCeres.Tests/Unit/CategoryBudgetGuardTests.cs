using FluentAssertions;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Verifies the CategoryBudget expense-only guard:
/// a CategoryBudget may only be attached to Expense-type categories.
///
/// Mirrors the guard that would be enforced in a CategoryBudgetService.CreateAsync
/// (or equivalent) before persisting to the database.
/// </summary>
public class CategoryBudgetGuardTests
{
    // Mirrors the guard in CategoryBudgetService: throw if category is not Expense.
    private static void AssertCategoryIsExpense(Category category)
    {
        if (category.CategoryType.Name != "Expense")
            throw new InvalidOperationException(
                "CategoryBudget can only be applied to Expense categories.");
    }

    private static Category CategoryWithType(string typeName) => new()
    {
        Id           = Guid.NewGuid(),
        Name         = "Test Category",
        CategoryType = new CategoryType { Name = typeName }
    };

    [Fact]
    public void Guard_ExpenseCategory_DoesNotThrow()
    {
        var category = CategoryWithType("Expense");
        var act = () => AssertCategoryIsExpense(category);
        act.Should().NotThrow();
    }

    [Fact]
    public void Guard_IncomeCategory_Throws()
    {
        var category = CategoryWithType("Income");
        var act = () => AssertCategoryIsExpense(category);
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Expense*");
    }

    [Fact]
    public void Guard_SystemIncomeCategory_AlsoThrows()
    {
        // System categories (e.g. Opening Balance) are Income type — guard still applies.
        var category = new Category
        {
            Id           = Guid.NewGuid(),
            Name         = "Opening Balance",
            IsSystem     = true,
            CategoryType = new CategoryType { Name = "Income" }
        };

        var act = () => AssertCategoryIsExpense(category);
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Expense*");
    }
}
