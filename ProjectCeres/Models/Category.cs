using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class Category : IOptionallyUserOwned
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
    /// <summary>NULL for system categories (IsSystem = true), shared across all users. Set for user-created categories.</summary>
    public Guid? UserId { get; set; }

    public CategoryType CategoryType { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
