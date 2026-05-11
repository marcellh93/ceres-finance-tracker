using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// Creates a per-user copy of every default category. Invoked at user registration
/// (both <c>AuthController.Register</c> and <c>AuthTestFixture.RegisterUserAsync</c>
/// call it after <see cref="Microsoft.AspNetCore.Identity.UserManager{T}.CreateAsync"/>).
/// Idempotent: if the user already has categories, the call is a no-op.
/// </summary>
public sealed class CategorySeedService(AppDbContext db)
{
    public async Task CopyDefaultsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        // Idempotency: the cross-tenant query is intentional here — we're checking whether
        // THIS user already has categories. The query filter (Task 9) is not yet on
        // Category at this stage (and Category is IOptionallyUserOwned with nullable UserId
        // until Task 16), so an explicit predicate is safest. IgnoreQueryFilters() also
        // belongs on the allow-list maintained by the architecture test (Task 10).
        var alreadyHas = await db.Categories
            .IgnoreQueryFilters()
            .AnyAsync(c => c.UserId == userId, ct);
        if (alreadyHas) return;

        var copies = Categories.Defaults.Select(d => new Category
        {
            Id             = Guid.NewGuid(),
            Name           = d.Name,
            CategoryTypeId = d.CategoryTypeId,
            IsActive       = true,
            IsSystem       = d.IsSystem,
            IsReserved     = d.IsReserved,
            LifestyleTag   = d.LifestyleTag,
            UserId         = userId,
        }).ToList();

        db.Categories.AddRange(copies);
        await db.SaveChangesAsync(ct);
    }
}
