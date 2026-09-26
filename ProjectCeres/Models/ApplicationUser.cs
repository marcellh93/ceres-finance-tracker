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
}
