namespace ProjectCeres.Models;

public class Settings
{
    public int Id { get; set; }
    public string NumberFormat { get; set; } = string.Empty;
    public string DateFormat { get; set; } = string.Empty;
public int DefaultCurrencyId { get; set; }

    public Currency DefaultCurrency { get; set; } = null!;
}
