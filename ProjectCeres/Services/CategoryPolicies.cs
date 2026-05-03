using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// Policy: which categories can be edited or deactivated. Pulled out of the service so
/// the rule lives in one place — controllers query it for pre-flight checks, the
/// service applies it on writes, and tests cover it directly without spinning up EF.
/// </summary>
public static class CategoryPolicies
{
    public const string SystemImmutableCode = "SYSTEM_CATEGORY_IMMUTABLE";
    public const string CategoryInUseCode   = "CATEGORY_IN_USE";

    /// <summary>Reserved system fallbacks. IsSystem is false on these so they appear in pickers, but they cannot be edited or deactivated.</summary>
    private static readonly Guid UncategorizedIncomeId  = new("20000000-0000-0000-0000-000000000025");
    private static readonly Guid UncategorizedExpenseId = new("20000000-0000-0000-0000-000000000026");

    public static bool IsReserved(Guid id) => id == UncategorizedIncomeId || id == UncategorizedExpenseId;

    public static Result CanEdit(Category category)
    {
        if (category.IsSystem || IsReserved(category.Id))
            return Result.Fail(SystemImmutableCode, "System categories cannot be modified.");
        return Result.Ok();
    }

    public static Result CanDeactivate(Category category, bool hasTransactions)
    {
        var edit = CanEdit(category);
        if (!edit.IsSuccess) return edit;
        if (hasTransactions)
            return Result.Fail(CategoryInUseCode, "This category has transactions. Reassign them before deactivating.");
        return Result.Ok();
    }

    /// <summary>System-immutability also blocks reactivate; system categories shouldn't
    /// be in the archived list in the first place, but this is defense in depth.</summary>
    public static Result CanReactivate(Category category) => CanEdit(category);
}
