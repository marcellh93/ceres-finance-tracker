using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class EmailVerifyRequest
{
    [Required]
    [StringLength(128)]
    public string Token { get; set; } = "";
}
