namespace ProjectCeres.ViewModels;

public class AccountLedgerViewModel
{
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string CurrencySymbol { get; set; } = string.Empty;
    public IEnumerable<LedgerEntryViewModel> Entries { get; set; } = [];
}

public class LedgerEntryViewModel
{
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty; // "Opening Balance", "Transaction", "Transfer", "Liability Payment"
    public string? CategoryName { get; set; }
    public decimal SignedAmount { get; set; }   // positive = adds to balance, negative = subtracts
    public decimal RunningBalance { get; set; }
}
