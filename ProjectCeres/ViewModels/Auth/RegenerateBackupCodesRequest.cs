using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels.Auth;

public sealed record RegenerateBackupCodesRequest(
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "TOTP code must be 6 digits.")]
    string TotpCode);
