using ProjectCeres.Data;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Common;

// Subclass that throws on every CopyDefaultsForUserAsync call. Used to assert that
// AuthController.Register rolls back the AspNetUsers row when seeding fails — i.e.
// registration is atomic across (a) UserManager.CreateAsync, (b) category seeding,
// (c) audit log entry. Without atomicity, a seeding failure would leave an orphan
// AspNetUsers row and the email permanently un-registrable (re-register would hit
// DuplicateUserName and silently return 204 per anti-enumeration).
public sealed class ThrowingCategorySeedService(AppDbContext db) : CategorySeedService(db)
{
    public override Task CopyDefaultsForUserAsync(Guid userId, CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated category seed failure for atomicity test.");
}
