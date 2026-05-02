using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class SavedReport : IUserOwned
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ReportTypeId { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? AccountId { get; set; }
    public int? CurrencyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid UserId { get; set; }

    public ReportType ReportType { get; set; } = null!;
    public Category? Category { get; set; }
    public Account? Account { get; set; }
    public Currency? Currency { get; set; }
}
