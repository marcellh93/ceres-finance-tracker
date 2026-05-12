using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class Category : IUserOwned
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryTypeId { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    /// <summary>
    /// Application-reserved row that must not be edited or deleted. Distinct from
    /// <see cref="IsSystem"/>: a reserved category is user-owned (each user has their own
    /// copy) but the application code depends on the row existing (e.g. "Uncategorized
    /// Income" / "Uncategorized Expense" as the fallback target when a category is removed).
    /// Stamped at registration time by <c>CategorySeedService</c> (Task 7); pre-Stage-7
    /// backfill stamps the two existing seeded rows in the EF migration.
    /// </summary>
    public bool IsReserved { get; set; }
    public string? LifestyleTag { get; set; }

    /// <summary>
    /// Owner. Set by CategorySeedService at registration (per-user copies of the canonical
    /// 26-entry Categories.Defaults list). Non-nullable post-Stage-7-Task-16; the legacy
    /// "Opening Balance" UserId-NULL row was stamped with the sentinel in
    /// StampOpeningBalanceWithSentinel and is remapped to the first real user by
    /// RemapSentinelToFirstUser.
    /// </summary>
    public Guid UserId { get; set; }

    public CategoryType CategoryType { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
