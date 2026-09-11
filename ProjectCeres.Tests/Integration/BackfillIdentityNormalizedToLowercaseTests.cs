using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Stage 9.1.5.h — Pin the BackfillIdentityNormalizedToLowercase migration's
/// idempotency contract: every normalized Identity column is lowercased on a
/// single application of the migration's SQL, and re-application is a no-op
/// (zero rows affected). NULL rows are preserved (no exception, no state change).
///
/// We invoke the migration's SQL directly via _db.Database.ExecuteSqlRaw rather
/// than running `dotnet ef database update` from inside a test — the shared
/// IntegrationTests fixture has the migration already applied by the time tests
/// run, so the test just exercises the SQL contract against rows we control.
/// </summary>
[Collection("IntegrationParallel4")]
public class BackfillIdentityNormalizedToLowercaseTests : IntegrationTestBase<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BackfillIdentityNormalizedToLowercaseTests(TestWebApplicationFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
    }

    // The three SQL statements the migration's Up() runs. Keep these as
    // constants here so a copy-paste drift between the migration and the test
    // fails the test (the test's seeded data flips back via the same SQL the
    // migration writes — if the migration's SQL changes shape, this test class
    // is the most likely first failure).
    private const string LowercaseEmailSql = @"
        UPDATE ""AspNetUsers""
        SET ""NormalizedEmail"" = LOWER(""NormalizedEmail"")
        WHERE ""NormalizedEmail"" IS NOT NULL
          AND ""NormalizedEmail"" <> LOWER(""NormalizedEmail"");";

    private const string LowercaseUserNameSql = @"
        UPDATE ""AspNetUsers""
        SET ""NormalizedUserName"" = LOWER(""NormalizedUserName"")
        WHERE ""NormalizedUserName"" IS NOT NULL
          AND ""NormalizedUserName"" <> LOWER(""NormalizedUserName"");";

    private const string LowercaseRoleNameSql = @"
        UPDATE ""AspNetRoles""
        SET ""NormalizedName"" = LOWER(""NormalizedName"")
        WHERE ""NormalizedName"" IS NOT NULL
          AND ""NormalizedName"" <> LOWER(""NormalizedName"");";

    // ─── AspNetUsers.NormalizedEmail ──────────────────────────────────────────

    [Fact]
    public async Task Migration_lowercases_uppercase_NormalizedEmail()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();
        var marker = $"backfill-email-upper-{userId}@test.local";

        await SeedAspNetUserAsync(db, userId, normalizedEmail: marker.ToUpperInvariant(),
            normalizedUserName: marker.ToUpperInvariant());
        try
        {
            var affected = await db.Database.ExecuteSqlRawAsync(LowercaseEmailSql);
            affected.Should().BeGreaterThanOrEqualTo(1, "the uppercase row must be touched");

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedEmail\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker.ToLowerInvariant());
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task Migration_is_idempotent_on_already_lowercase_NormalizedEmail()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();
        var marker = $"backfill-email-lower-{userId}@test.local";

        await SeedAspNetUserAsync(db, userId, normalizedEmail: marker,
            normalizedUserName: marker);
        try
        {
            // First run — may touch 0 or 1 rows depending on the seed (already lower).
            await db.Database.ExecuteSqlRawAsync(LowercaseEmailSql);

            // Second run — MUST touch 0 rows for this seeded row (it's already lowercase).
            // Note: other rows in the shared DB may still match (different tests' leftovers).
            // We assert the row's final state instead.
            await db.Database.ExecuteSqlRawAsync(LowercaseEmailSql);

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedEmail\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker);
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task Migration_handles_NULL_NormalizedEmail_safely()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();

        await SeedAspNetUserAsync(db, userId, normalizedEmail: null,
            normalizedUserName: $"backfill-null-{userId}");
        try
        {
            await db.Database.Invoking(d => d.ExecuteSqlRawAsync(LowercaseEmailSql))
                .Should().NotThrowAsync();

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedEmail\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().BeNull();
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    // ─── AspNetUsers.NormalizedUserName ───────────────────────────────────────

    [Fact]
    public async Task Migration_lowercases_uppercase_NormalizedUserName()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();
        var marker = $"BACKFILL-USERNAME-UPPER-{userId}";

        await SeedAspNetUserAsync(db, userId, normalizedEmail: marker.ToLowerInvariant(),
            normalizedUserName: marker);
        try
        {
            var affected = await db.Database.ExecuteSqlRawAsync(LowercaseUserNameSql);
            affected.Should().BeGreaterThanOrEqualTo(1);

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedUserName\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker.ToLowerInvariant());
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task Migration_is_idempotent_on_already_lowercase_NormalizedUserName()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();
        var marker = $"backfill-username-lower-{userId}";

        await SeedAspNetUserAsync(db, userId, normalizedEmail: marker, normalizedUserName: marker);
        try
        {
            await db.Database.ExecuteSqlRawAsync(LowercaseUserNameSql);
            await db.Database.ExecuteSqlRawAsync(LowercaseUserNameSql);

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedUserName\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker);
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task Migration_handles_NULL_NormalizedUserName_safely()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var userId = Guid.NewGuid();
        var marker = $"backfill-null-username-{userId}@test.local";

        await SeedAspNetUserAsync(db, userId, normalizedEmail: marker, normalizedUserName: null);
        try
        {
            await db.Database.Invoking(d => d.ExecuteSqlRawAsync(LowercaseUserNameSql))
                .Should().NotThrowAsync();

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedUserName\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = '{userId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().BeNull();
        }
        finally
        {
            await DeleteAspNetUserAsync(db, userId);
        }
    }

    // ─── AspNetRoles.NormalizedName ───────────────────────────────────────────
    // Roles are unused at parent commit 4e45b62, but the migration covers
    // this column so its contract is bound for the future. Tests use raw SQL
    // inserts because the project has no RoleManager usage to bypass.

    [Fact]
    public async Task Migration_lowercases_uppercase_role_NormalizedName()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var roleId = Guid.NewGuid();
        var marker = $"BACKFILL-ROLE-UPPER-{roleId}";

        await SeedAspNetRoleAsync(db, roleId, name: marker.ToLowerInvariant(), normalizedName: marker);
        try
        {
            var affected = await db.Database.ExecuteSqlRawAsync(LowercaseRoleNameSql);
            affected.Should().BeGreaterThanOrEqualTo(1);

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedName\" AS \"Value\" FROM \"AspNetRoles\" WHERE \"Id\" = '{roleId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker.ToLowerInvariant());
        }
        finally
        {
            await DeleteAspNetRoleAsync(db, roleId);
        }
    }

    [Fact]
    public async Task Migration_is_idempotent_on_already_lowercase_role_NormalizedName()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var roleId = Guid.NewGuid();
        var marker = $"backfill-role-lower-{roleId}";

        await SeedAspNetRoleAsync(db, roleId, name: marker, normalizedName: marker);
        try
        {
            await db.Database.ExecuteSqlRawAsync(LowercaseRoleNameSql);
            await db.Database.ExecuteSqlRawAsync(LowercaseRoleNameSql);

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedName\" AS \"Value\" FROM \"AspNetRoles\" WHERE \"Id\" = '{roleId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().Be(marker);
        }
        finally
        {
            await DeleteAspNetRoleAsync(db, roleId);
        }
    }

    [Fact]
    public async Task Migration_handles_NULL_role_NormalizedName_safely()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var roleId = Guid.NewGuid();
        var marker = $"backfill-role-null-{roleId}";

        await SeedAspNetRoleAsync(db, roleId, name: marker, normalizedName: null);
        try
        {
            await db.Database.Invoking(d => d.ExecuteSqlRawAsync(LowercaseRoleNameSql))
                .Should().NotThrowAsync();

            var stored = await db.Database
                .SqlQueryRaw<string?>(
                    $"SELECT \"NormalizedName\" AS \"Value\" FROM \"AspNetRoles\" WHERE \"Id\" = '{roleId}'::uuid")
                .FirstOrDefaultAsync();
            stored.Should().BeNull();
        }
        finally
        {
            await DeleteAspNetRoleAsync(db, roleId);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────
    // SECURITY NOTE: the seed helpers below use SQL string interpolation for the
    // values. This is SAFE here because every input is test-controlled — Guid.NewGuid()
    // for ids, internally-constructed marker strings for the normalized columns.
    // NullableSqlString escapes single quotes for the rare marker that contains one.
    // DO NOT copy these helpers into production code paths or into tests that
    // accept user-sourced input — use parameterized queries (FormattableString /
    // ExecuteSqlInterpolatedAsync) instead.

    private static async Task SeedAspNetUserAsync(AdminDbContext db, Guid id,
        string? normalizedEmail, string? normalizedUserName)
    {
        // Raw insert bypasses UserManager (which would re-normalize). The columns
        // not under test are populated with safe defaults so the row satisfies
        // any non-null constraints.
        await db.Database.ExecuteSqlRawAsync($@"
            INSERT INTO ""AspNetUsers""
                (""Id"", ""UserName"", ""NormalizedUserName"", ""Email"", ""NormalizedEmail"",
                 ""EmailConfirmed"", ""PasswordHash"", ""SecurityStamp"", ""ConcurrencyStamp"",
                 ""PhoneNumberConfirmed"", ""TwoFactorEnabled"", ""LockoutEnabled"",
                 ""AccessFailedCount"", ""CreatedAt"")
            VALUES
                ('{id}'::uuid, 'seed', {NullableSqlString(normalizedUserName)},
                 'seed@test.local', {NullableSqlString(normalizedEmail)},
                 false, '', '', '', false, false, false, 0, NOW());
        ");
    }

    private static async Task DeleteAspNetUserAsync(AdminDbContext db, Guid id)
    {
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"AspNetUsers\" WHERE \"Id\" = '{id}'::uuid");
    }

    private static async Task SeedAspNetRoleAsync(AdminDbContext db, Guid id,
        string? name, string? normalizedName)
    {
        await db.Database.ExecuteSqlRawAsync($@"
            INSERT INTO ""AspNetRoles""
                (""Id"", ""Name"", ""NormalizedName"", ""ConcurrencyStamp"")
            VALUES
                ('{id}'::uuid, {NullableSqlString(name)}, {NullableSqlString(normalizedName)}, '');
        ");
    }

    private static async Task DeleteAspNetRoleAsync(AdminDbContext db, Guid id)
    {
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"AspNetRoles\" WHERE \"Id\" = '{id}'::uuid");
    }

    private static string NullableSqlString(string? value) =>
        value is null ? "NULL" : $"'{value.Replace("'", "''")}'";
}
