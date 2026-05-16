using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tools;

/// <summary>
/// Development-only bootstrap helper. Creates the first user account and remaps any
/// sentinel-tagged (Phase 1/2) data onto that user.
///
/// <para>
/// Invoked via: <c>dotnet run --project ProjectCeres -- --seed-dev-user</c>
/// </para>
///
/// <para>
/// This helper exists because:
/// <list type="bullet">
///   <item>The <c>RemapSentinelToFirstUser</c> migration was a no-op on first apply
///         (zero users in AspNetUsers at migration time).</item>
///   <item>The SPA register flow (Stage 9 Phase 2) hasn't shipped yet.</item>
///   <item>Even when it ships, login is gated on email-confirmation which also hasn't
///         shipped, so there is no in-band path to a confirmed first-user account.</item>
/// </list>
/// </para>
///
/// <para>
/// Exit codes: 0 ok | 1 user-create fail | 2 env gate | 3 remap pre-check | 4 remap post-check.
/// </para>
/// </summary>
public static class SeedDevUser
{
    private const string TargetEmail = "marcelljesus1218@gmail.com";

    // Sentinel UUID from Phase 1/2 — same as RemapSentinelToFirstUser migration.
    private static readonly Guid Sentinel = new("00000000-0000-0000-0000-000000000001");

    // 32-char password alphabet: no easily-confused chars (0/O/I/l/1).
    private const string PasswordAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%^&*";

    // All 16 user-owned tables: 14 from the original migration + TransactionAttachments
    // + TransferAttachments (added in Stage 7.5 with attachment support).
    private static readonly string[] UserOwnedTables =
    [
        "Accounts",
        "Categories",
        "Transactions",
        "Transfers",
        "LiabilityPayments",
        "CategoryBudgets",
        "Budgets",
        "RecurringTransactions",
        "SavedReports",
        "ImportProfiles",
        "ImportStagedTransactions",
        "ImportStagedTransfers",
        "ImportTransferExclusions",
        "Settings",
        "TransactionAttachments",
        "TransferAttachments",
    ];

    public static async Task<int> RunAsync(WebApplicationBuilder builder, string[] args)
    {
        // === Environment gate ===
        if (!builder.Environment.IsDevelopment())
        {
            Console.Error.WriteLine(
                "ERROR: SeedDevUser is gated on Development environment. " +
                "ASPNETCORE_ENVIRONMENT is not 'Development'. Aborting.");
            return 2;
        }

        // Build and start the application to access DI services. We build the full
        // app (same as normal startup) so all services — UserManager, CategorySeedService,
        // AdminDbContext — are wired exactly as production uses them.
        var app = builder.Build();

        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var userManager    = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var categorySeed   = sp.GetRequiredService<CategorySeedService>();
        var adminDb        = sp.GetRequiredService<AdminDbContext>();
        var bgJobScope     = sp.GetRequiredService<IBackgroundJobScope>();
        var logger         = sp.GetRequiredService<ILogger<Program>>();

        // === Step 1: User creation (idempotent) ===

        var existingUser = await userManager.FindByEmailAsync(TargetEmail);
        Guid userId;

        if (existingUser is not null)
        {
            Console.WriteLine($"[SeedDevUser] User {TargetEmail} already exists; skipping creation; running remap-only.");
            userId = existingUser.Id;
        }
        else
        {
            // Generate a 32-char cryptographically-random password from a clean alphabet.
            var password = RandomNumberGenerator.GetString(PasswordAlphabet, 32);

            // === PRINT THE PASSWORD BEFORE THE REMAP — survives even a remap rollback ===
            Console.WriteLine();
            Console.WriteLine("=============================================================");
            Console.WriteLine("  SAVE THIS PASSWORD NOW — it will not be shown again.");
            Console.WriteLine();
            Console.WriteLine($"  Email:    {TargetEmail}");
            Console.WriteLine($"  Password: {password}");
            Console.WriteLine("=============================================================");
            Console.WriteLine();

            var user = new ApplicationUser { UserName = TargetEmail, Email = TargetEmail };
            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                Console.Error.WriteLine("[SeedDevUser] ERROR: UserManager.CreateAsync failed:");
                foreach (var error in createResult.Errors)
                {
                    Console.Error.WriteLine($"  [{error.Code}] {error.Description}");
                }
                return 1;
            }

            // Confirm email via the official Identity token path — mirrors AuthTestFixture lines 36-37.
            var token    = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirm  = await userManager.ConfirmEmailAsync(user, token);
            if (!confirm.Succeeded)
            {
                Console.Error.WriteLine("[SeedDevUser] ERROR: ConfirmEmailAsync failed:");
                foreach (var error in confirm.Errors)
                {
                    Console.Error.WriteLine($"  [{error.Code}] {error.Description}");
                }
                return 1;
            }

            userId = user.Id;
            Console.WriteLine($"[SeedDevUser] User created and confirmed. Id = {userId}");
        }

        // === Step 2: Seed default categories ===
        // Route through IBackgroundJobScope.RunAsync so HttpContextCurrentUserAccessor
        // resolves to UserContext.Resolved(userId) via the approved EnterAs path inside
        // BackgroundJobScope. This is required for the RowLevelSecurityInterceptor to set
        // the GUC that allows the RLS INSERT policy on the Categories table.
        await bgJobScope.RunAsync(userId, "SeedDevUser.CategorySeed",
            () => categorySeed.CopyDefaultsForUserAsync(userId));
        Console.WriteLine($"[SeedDevUser] Default categories seeded (idempotent — no-op if already present).");

        // === Step 3: Sentinel remap ===

        // Pre-check: exactly one user in AspNetUsers (the one we just created/confirmed).
        // If there are more, we cannot safely remap — ambiguous target.
        // AdminDbContext uses ceres_admin (BYPASSRLS). AspNetUsers has no EF query filter
        // (Identity table, not a user-owned table), so no IgnoreQueryFilters() needed.
        var userCount = await adminDb.Users.CountAsync();

        if (userCount > 1)
        {
            Console.Error.WriteLine(
                $"[SeedDevUser] ERROR: AspNetUsers contains {userCount} rows — expected exactly 1. " +
                "Remap is ambiguous with multiple users. Aborting (exit 3).");
            return 3;
        }

        // Count sentinel rows across all 16 tables for the pre-check + progress report.
        var sentinelStr    = Sentinel.ToString("D");
        var preCount       = await CountSentinelRowsAsync(adminDb, sentinelStr);

        if (preCount == 0)
        {
            Console.WriteLine("[SeedDevUser] Remap: zero sentinel-tagged rows found — nothing to remap. Clean exit.");
            return 0;
        }

        Console.WriteLine($"[SeedDevUser] Remap: {preCount} sentinel row(s) found. Beginning remap to user {userId}...");

        // Remap inside a single Postgres transaction. On post-check failure the
        // transaction rolls back automatically (DbContext disposes the transaction).
        await using var tx = await adminDb.Database.BeginTransactionAsync();
        try
        {
            foreach (var table in UserOwnedTables)
            {
                var sql = $"""
                    UPDATE "{table}" SET "UserId" = '{userId:D}' WHERE "UserId" = '{sentinelStr}'
                    """;
                await adminDb.Database.ExecuteSqlRawAsync(sql);
            }

            // Post-check: zero sentinel rows must remain across all 16 tables.
            var postCount = await CountSentinelRowsAsync(adminDb, sentinelStr);
            if (postCount > 0)
            {
                Console.Error.WriteLine(
                    $"[SeedDevUser] ERROR: Post-check failed — {postCount} sentinel row(s) remain after UPDATE. " +
                    "Rolling back transaction (exit 4).");
                await tx.RollbackAsync();
                return 4;
            }

            await tx.CommitAsync();
            Console.WriteLine($"[SeedDevUser] Remap complete: {preCount} sentinel row(s) moved to user {userId}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SeedDevUser] ERROR during remap: {ex.Message}");
            await tx.RollbackAsync();
            return 4;
        }

        Console.WriteLine("[SeedDevUser] Done. Exit 0.");
        return 0;
    }

    /// <summary>
    /// Counts sentinel-tagged rows across all 16 user-owned tables using the admin
    /// connection (BYPASSRLS). Used for both the pre-check and the post-check.
    /// </summary>
    private static async Task<long> CountSentinelRowsAsync(AdminDbContext adminDb, string sentinelStr)
    {
        long total = 0;
        foreach (var table in UserOwnedTables)
        {
            // FormattableString overload of FromSql is not available for arbitrary table
            // names. Use ExecuteSqlRaw — sentinel UUID is a constant, not user input.
            var sql = $"""SELECT COUNT(*) FROM "{table}" WHERE "UserId" = '{sentinelStr}'""";

            // EF Core doesn't expose a scalar-returning ExecuteSql; use Npgsql directly
            // via the underlying connection.
            var conn = adminDb.Database.GetDbConnection();
            var wasOpen = conn.State == System.Data.ConnectionState.Open;
            if (!wasOpen) await conn.OpenAsync();
            try
            {
                await using var cmd = conn.CreateCommand();
                // Attach to any active transaction so the count reflects in-transaction state.
                if (adminDb.Database.CurrentTransaction is { } efTx)
                    cmd.Transaction = efTx.GetDbTransaction();
                cmd.CommandText = sql;
                var result = await cmd.ExecuteScalarAsync();
                total += Convert.ToInt64(result);
            }
            finally
            {
                if (!wasOpen) await conn.CloseAsync();
            }
        }
        return total;
    }
}
