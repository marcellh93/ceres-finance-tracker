using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class Category : IOptionallyUserOwned
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryTypeId { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public string? LifestyleTag { get; set; }
    /// <summary>NULL for system categories (IsSystem = true), shared across all users. Set for user-created categories.</summary>
    public Guid? UserId { get; set; }

    public CategoryType CategoryType { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
