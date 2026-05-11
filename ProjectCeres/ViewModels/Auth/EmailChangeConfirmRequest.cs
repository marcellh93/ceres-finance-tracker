using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class EmailChangeConfirmRequest
{
    [Required]
    [StringLength(128)]
    public string Token { get; set; } = "";
}
