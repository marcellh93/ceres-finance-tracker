using System.Text.RegularExpressions;

namespace ProjectCeres.Common.Authentication;

public static class MfaConstants
{
    public const string Issuer = "Ceres";

    /// <summary>10 backup codes per batch.</summary>
    public const int BackupCodeBatchSize = 10;

    /// <summary>16 chars from Crockford base-32 → ~80 bits entropy.</summary>
    public const int BackupCodeLength = 16;

    /// <summary>
    /// Crockford base-32 alphabet — 32 chars, no I/L/O/U (visually ambiguous letters).
    /// All 10 digits are kept so a 16-digit string is technically a valid backup code,
    /// which is why TOTP shape detection runs first (^\d{6}$) before backup-code shape.
    /// </summary>
    public const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Replay window: any code accepted within this duration cannot be re-used.</summary>
    public static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(2);

    /// <summary>Six-digit numeric TOTP shape. Tested first; if it matches, route to the TOTP path.</summary>
    public static readonly Regex TotpCodeShape = new(@"^\d{6}$", RegexOptions.Compiled);

    /// <summary>
    /// Backup-code shape after stripping `-` separators and uppercasing.
    /// Tests strict 16-char Crockford base-32 (excludes I/L/O/U).
    /// </summary>
    public static readonly Regex BackupCodeShape = new(
        @"^[0-9A-HJKMNP-TV-Z]{16}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
