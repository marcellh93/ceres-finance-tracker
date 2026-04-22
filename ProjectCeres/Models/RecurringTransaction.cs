using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectCeres.Models;

public class RecurringTransaction
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")]
    public decimal? EstimatedAmount { get; set; }
    public Guid AccountId { get; set; }
    public Guid CategoryId { get; set; }
    public Frequency Frequency { get; set; }
    public int? DayOfPeriod { get; set; }
    public DateOnly NextDueDate { get; set; }
    public bool IsActive { get; set; }
    public string ReminderBehaviour { get; set; } = "SnapToCalendarDay";

    public Account Account { get; set; } = null!;
    public Category Category { get; set; } = null!;
}
