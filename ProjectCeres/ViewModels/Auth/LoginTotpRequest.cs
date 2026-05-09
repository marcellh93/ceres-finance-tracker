using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class LoginTotpRequest
{
    /// <summary>
    /// Either a 6-digit TOTP code or a 16-character Crockford backup code
    /// (with or without `-` separators). The endpoint detects shape and routes.
    /// </summary>
    [Required, StringLength(32)]
    public string Code { get; set; } = "";
}
