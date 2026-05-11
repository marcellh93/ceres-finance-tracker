namespace ProjectCeres.Common;

/// <summary>
/// Canonical default-category list. Used by <c>CategorySeedService</c> at registration
/// to create a per-user copy of every default category. The historic single-user seed
/// in <c>AppDbContext.SeedCategories</c> mirrors this list — they stay in lock-step until
/// Stage 7 Commit 2 deletes the in-DbContext seed (Task 17).
/// </summary>
public static class Categories
{
    public sealed record DefaultCategory(
        string Name,
        int CategoryTypeId,
        bool IsSystem,
        bool IsReserved,
        string? LifestyleTag);

    public static IReadOnlyList<DefaultCategory> Defaults { get; } = new[]
    {
        new DefaultCategory("Opening Balance",        1, IsSystem: true,  IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Salary",                 1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Freelance Income",       1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Rental Income",          1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Investment Income",      1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Business Income",        1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Other Income",           1, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Housing / Rent",         2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Utilities",              2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Groceries",              2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Transport",              2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Fuel",                   2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Healthcare",             2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Insurance",              2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Subscriptions",          2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Dining Out",             2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Entertainment",          2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Clothing",               2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Personal Care",          2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Education",              2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Travel",                 2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Home & Garden",          2, IsSystem: false, IsReserved: false, LifestyleTag: "Needs"),
        new DefaultCategory("Gifts & Donations",      2, IsSystem: false, IsReserved: false, LifestyleTag: "Wants"),
        new DefaultCategory("Other Expenses",         2, IsSystem: false, IsReserved: false, LifestyleTag: null),
        new DefaultCategory("Uncategorized Income",   1, IsSystem: false, IsReserved: true,  LifestyleTag: null),
        new DefaultCategory("Uncategorized Expense",  2, IsSystem: false, IsReserved: true,  LifestyleTag: null),
    };
}
