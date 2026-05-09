using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class EnrollVerifyRequest
{
    [Required, StringLength(8), RegularExpression(@"^\d{6}$")]
    public string Code { get; set; } = "";
}
