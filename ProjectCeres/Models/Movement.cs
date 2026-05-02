using System.ComponentModel.DataAnnotations.Schema;
using ProjectCeres.Common;

namespace ProjectCeres.Models;

public abstract class Movement : IUserOwned
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public bool IsCleared { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid UserId { get; set; }
}
