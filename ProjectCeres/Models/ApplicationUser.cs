using Microsoft.AspNetCore.Identity;

namespace ProjectCeres.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
