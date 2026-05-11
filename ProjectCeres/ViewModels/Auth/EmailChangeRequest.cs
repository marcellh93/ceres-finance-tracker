using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class EmailChangeRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string NewEmail { get; set; } = "";
}
