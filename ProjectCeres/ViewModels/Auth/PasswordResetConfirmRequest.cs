using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed class PasswordResetConfirmRequest
{
    [Required]
    [StringLength(128)]
    public string Token { get; set; } = "";

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = "";

    /// <summary>Six-digit TOTP code. Required when the user has TwoFactorEnabled = true.
    /// Backup codes are NOT accepted in the reset flow per ADR-0069. The DTO length is
    /// permissive (32) so any backup-code-shaped input reaches the service-side
    /// MfaConstants.TotpCodeShape check, which rejects it with INVALID_MFA_CODE (401).</summary>
    [StringLength(32)]
    public string? TotpCode { get; set; }
}
