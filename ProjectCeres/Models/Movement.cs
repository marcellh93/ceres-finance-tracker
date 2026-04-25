using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectCeres.Models;

public abstract class Movement
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public bool IsCleared { get; set; }
    public DateTime CreatedAt { get; set; }
}
