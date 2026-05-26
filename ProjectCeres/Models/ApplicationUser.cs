using Microsoft.AspNetCore.Identity;
using ProjectCeres.Analyzers.Annotations;

namespace ProjectCeres.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    [AllowsWallClock("entity property initialiser; EF Core materializer cannot inject TimeProvider")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
