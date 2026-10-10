using Microsoft.AspNetCore.Identity;
using ProjectCeres.Analyzers.Annotations;

namespace ProjectCeres.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    [AllowsWallClock("entity property initialiser; EF Core materializer cannot inject TimeProvider")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Stage 13.9 right-to-erasure: set when an ErasureRequest is sealed (soft-lockout
    // start), cleared if cancelled. ErasedAt is set once the worker completes erasure.
    public DateTime? SealedAt { get; set; }
    public DateTime? ErasedAt { get; set; }

    /// <summary>Last successful login (password, TOTP, or backup code). Measures inactivity for Stage 13.b.</summary>
    public DateTime LastLoginAt { get; set; }

    /// <summary>Set when the 12-month inactivity warning is sent; cleared to null on every login.</summary>
    public DateTime? InactivityWarningSentAt { get; set; }
}
