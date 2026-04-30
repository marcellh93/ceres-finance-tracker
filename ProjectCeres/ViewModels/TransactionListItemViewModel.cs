namespace ProjectCeres.ViewModels;

/// <summary>
/// Unified read model for the Transactions Index list.
/// Represents either a regular Transaction or a LiabilityPayment row.
/// </summary>
public class TransactionListItemViewModel
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }

    /// <summary>"Regular" or "LiabilityPayment"</summary>
    public string TransactionType { get; set; } = TransactionTypes.Regular;

    // Regular transaction fields
    public string? AccountName { get; set; }
    public string? CurrencySymbol { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryTypeName { get; set; }

    public bool IsCleared { get; set; }
    public bool NeedsReview { get; set; }

    // LiabilityPayment fields
    public string? AssetAccountName { get; set; }
    public string? LiabilityAccountName { get; set; }
}
