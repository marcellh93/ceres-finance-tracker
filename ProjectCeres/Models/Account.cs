namespace ProjectCeres.Models;

public class Account
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AccountTypeId { get; set; }
    public int CurrencyId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }

    public AccountType AccountType { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
