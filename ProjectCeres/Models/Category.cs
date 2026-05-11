using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class Category : IUserOwned, IOptionallyUserOwned
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
    /// <summary>NULL for the single legacy "Opening Balance" row (UserId = null) until Task 15
    /// remaps it to a real user. Set for all user-owned categories (all rows after Task 7).</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Stage 7 bridge: IUserOwned requires a non-nullable Guid, but Category.UserId is still
    /// nullable until Task 16 remaps the sentinel-stamped data and drops the nullability.
    /// The explicit interface implementation returns Guid.Empty for the single legacy
    /// UserId-null "Opening Balance" row — that row is remapped to a real user in Task 15.
    /// </summary>
    Guid IUserOwned.UserId => UserId ?? Guid.Empty;

    public CategoryType CategoryType { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
