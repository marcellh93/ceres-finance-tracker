namespace ProjectCeres.ViewModels;

public enum MovementType
{
    Transaction,
    Transfer,
    LiabilityPayment
}

public class MovementListItemViewModel
{
    public Guid Id { get; set; }
    public MovementType MovementType { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public bool IsCleared { get; set; }
    public DateTime CreatedAt { get; set; }

    // Transaction fields
    public Guid? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? CurrencySymbol { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryTypeName { get; set; }

    // Transfer fields
    public Guid? SourceAccountId { get; set; }
    public string? SourceAccountName { get; set; }
    public Guid? DestAccountId { get; set; }
    public string? DestAccountName { get; set; }

    // LiabilityPayment fields
    public Guid? AssetAccountId { get; set; }
    public string? AssetAccountName { get; set; }
    public Guid? LiabilityAccountId { get; set; }
    public string? LiabilityAccountName { get; set; }
}
