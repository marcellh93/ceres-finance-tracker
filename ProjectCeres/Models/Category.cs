namespace ProjectCeres.Models;

public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryTypeId { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public string? LifestyleTag { get; set; }

    public CategoryType CategoryType { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
