using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

/// <summary>Body for POST /api/profile/erasure/cancel. Stage 13.9 Task 9.</summary>
public sealed class ErasureCancelRequest
{
    [Required]
    [StringLength(128)]
    public string Token { get; set; } = "";
}
