using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class ReauthRequest
{
    /// <summary>Plain-text password. Required when the user does NOT have MFA enabled;
    /// ignored otherwise.</summary>
    [StringLength(128)]
    public string? Password { get; set; }

    /// <summary>TOTP code. Required when the user has TwoFactorEnabled = true;
    /// ignored otherwise. Backup codes are NOT accepted at reauth (recovery path is
    /// /login/totp). Permissive bound (32 chars) so service-side
    /// MfaConstants.TotpCodeShape rejects backup-code-shaped submissions with 401.</summary>
    [StringLength(32)]
    public string? TotpCode { get; set; }
}
