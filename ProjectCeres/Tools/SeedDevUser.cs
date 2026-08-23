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
/// Development-only bootstrap helper. Creates a confirmed dev user account and remaps any
/// sentinel-tagged (Phase 1/2) data onto that user across all 16 user-owned tables.
///
/// <para>
/// When to use this:
/// <list type="bullet">
///   <item>AspNetUsers is empty and you have pre-Stage-7 sentinel-tagged data that needs
///         to be owned by a real user account.</item>
///   <item>After a dev DB wipe — re-run to re-create the account; sentinel remap becomes
///         a no-op if no sentinel rows remain.</item>
/// </list>
/// </para>
///
/// <para>
/// Gated on <c>IsDevelopment</c>. Will not run in Staging or Production.
/// </para>
///
/// <para>
/// CLI invocation:
/// <code>
/// dotnet run --project ProjectCeres --launch-profile https -- --seed-dev-user --email &lt;addr&gt; --generate-password
/// dotnet run --project ProjectCeres --launch-profile https -- --seed-dev-user --email &lt;addr&gt; --password &lt;pw&gt;
/// </code>
/// </para>
///
/// <para>
/// Exit codes: 0 ok | 1 user-create fail | 2 env gate | 3 remap pre-check | 4 remap post-check
/// | 5 CLI validation | 6 admin-grant fail (the account exists and is usable; only the role is missing).
/// </para>
/// </summary>
public static class SeedDevUser
{
    /// <summary>Parsed and validated CLI arguments for this helper.</summary>
    private sealed record Args(string Email, string? PlainPassword, bool GeneratePassword, bool Admin);

    // Sentinel UUID from Phase 1/2 — same as RemapSentinelToFirstUser migration.
    private static readonly Guid Sentinel = new("00000000-0000-0000-0000-000000000001");

    // Table names are interpolated into raw SQL (EF cannot parameterize identifiers).
    // They come from UserOwnedModel.FinanceTables, never user input — this guard pins
    // that invariant so a future caller can't introduce an injection vector. (9.1.6.a)
    public static string AssertKnownTable(string table, ISet<string> allowed)
    {
        if (!allowed.Contains(table))
            throw new InvalidOperationException(
                $"Refusing to interpolate '{table}': not in the user-owned table allow-list.");
        return table;
    }

    // 32-char password alphabet: no easily-confused chars (0/O/I/l/1).
    private const string PasswordAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%^&*";

    // Project's Identity password floor (see IdentityConfig). Checked here to fail fast
    // at the CLI surface rather than waiting for UserManager to reject the value.
    private const int MinPasswordLength = 15;

    public static async Task<int> RunAsync(WebApplicationBuilder builder, string[] args)
    {
        // === CLI validation (before touching DI/app) ===
        var parsed = ParseArgs(args, out int validationExitCode);
        if (parsed is null)
            return validationExitCode;

        // === Environment gate ===
        // Development is always allowed. Outside Development the only permitted use is
        // creating the FIRST admin — the bootstrap case, which closes as soon as one
        // exists. Shell access to the server is the practical safeguard.
        var isDevelopment = builder.Environment.IsDevelopment();
        if (!isDevelopment && !parsed.Admin)
        {
            Console.Error.WriteLine(
                "ERROR: this tool is gated on Development unless --admin is used to create " +
                "the first admin account. ASPNETCORE_ENVIRONMENT is not 'Development'. Aborting.");
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

        var adminRoles = sp.GetRequiredService<ProjectCeres.Admin.AdminRoleService>();

        if (!isDevelopment && parsed.Admin && await adminRoles.AnyAdminExistsAsync())
        {
            Console.Error.WriteLine(
                "ERROR: an admin account already exists. Outside Development this tool may " +
                "only create the FIRST admin; promote further admins through the app. Aborting.");
            return 2;
        }

        // === Step 1: User creation (idempotent) ===

        var existingUser = await userManager.FindByEmailAsync(parsed.Email);
        Guid userId;

        if (existingUser is not null)
        {
            Console.WriteLine($"[SeedDevUser] User {parsed.Email} already exists; skipping creation; running remap-only.");
            // Skip password generation entirely on the existing-user path — generating a
            // password that won't be used would be misleading and wasteful.
            userId = existingUser.Id;
        }
        else
        {
            // Determine the password to use.
            string password;
            if (parsed.GeneratePassword)
            {
                // Generate a 32-char cryptographically-random password from a clean alphabet.
                password = RandomNumberGenerator.GetString(PasswordAlphabet, 32);

                // === PRINT THE PASSWORD BEFORE THE REMAP — survives even a remap rollback ===
                Console.WriteLine();
                Console.WriteLine("=============================================================");
                Console.WriteLine("  SAVE THIS PASSWORD NOW — it will not be shown again.");
                Console.WriteLine();
                Console.WriteLine($"  Email:    {parsed.Email}");
                Console.WriteLine($"  Password: {password}");
                Console.WriteLine("=============================================================");
                Console.WriteLine();
            }
            else
            {
                // --password <pw> path: value already validated >=15 chars by ParseArgs.
                password = parsed.PlainPassword!;
            }

            var user = new ApplicationUser { UserName = parsed.Email, Email = parsed.Email };
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

        // === Step 3: Grant the Admin role ===
        if (parsed.Admin)
        {
            var granted = await adminRoles.GrantAsync(userId);
            if (!granted)
            {
                // Exit 6, not 1: the account exists and is usable, only the role is missing.
                // A retry needs to grant the role, not re-create the account.
                Console.Error.WriteLine(
                    $"[SeedDevUser] ERROR: account {parsed.Email} exists but the Admin role could not be granted.");
                return 6;
            }
            Console.WriteLine($"[SeedDevUser] Granted Admin to {parsed.Email}.");
        }

        // === Step 4: Sentinel remap ===

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
            // Stage 9.5b: the finance/attachment table set is derived from the EF model
            // (UserOwnedModel.FinanceTables), not a hand-typed list. It excludes the
            // auth-internal tables — the dev sentinel remap only touches Phase-1/2 data.
            var allowedTables = UserOwnedModel.FinanceTables(adminDb.Model)
                .Select(t => t.PostgresTableName)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var table in UserOwnedModel.FinanceTables(adminDb.Model).Select(t => t.PostgresTableName))
            {
                AssertKnownTable(table, allowedTables);
                await adminDb.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "{table}" SET "UserId" = {userId} WHERE "UserId" = {Sentinel}""");
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
    /// Parses and validates the args slice that the dispatch in Program.cs passes in
    /// (everything after <c>--seed-dev-user</c>).
    /// Returns a populated <see cref="Args"/> record on success, or null on validation
    /// failure (in which case <paramref name="errorExitCode"/> is set to 5 and a usage
    /// block has been printed to <c>Console.Error</c>).
    /// </summary>
    private static Args? ParseArgs(string[] args, out int errorExitCode)
    {
        errorExitCode = 0;

        string? email         = null;
        string? plainPassword = null;
        bool    generatePw    = false;
        bool    admin         = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                    PrintUsage();
                    errorExitCode = 0;
                    return null;

                case "--email":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("ERROR: --email requires a value.");
                        PrintUsage();
                        errorExitCode = 5;
                        return null;
                    }
                    email = args[++i];
                    break;

                case "--password":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("ERROR: --password requires a value.");
                        PrintUsage();
                        errorExitCode = 5;
                        return null;
                    }
                    plainPassword = args[++i];
                    break;

                case "--generate-password":
                    generatePw = true;
                    break;

                case "--admin":
                    admin = true;
                    break;

                default:
                    Console.Error.WriteLine($"ERROR: Unknown flag '{args[i]}'.");
                    PrintUsage();
                    errorExitCode = 5;
                    return null;
            }
        }

        // --- Validate: --email required, non-empty, contains '@' ---
        if (string.IsNullOrEmpty(email))
        {
            Console.Error.WriteLine("ERROR: --email is required.");
            PrintUsage();
            errorExitCode = 5;
            return null;
        }

        if (!email.Contains('@'))
        {
            Console.Error.WriteLine($"ERROR: --email value '{email}' does not look like an email address (missing '@').");
            PrintUsage();
            errorExitCode = 5;
            return null;
        }

        // --- Validate: exactly one of --password or --generate-password ---
        if (!generatePw && plainPassword is null)
        {
            Console.Error.WriteLine("ERROR: Provide exactly one of --generate-password or --password <pw>.");
            PrintUsage();
            errorExitCode = 5;
            return null;
        }

        if (generatePw && plainPassword is not null)
        {
            Console.Error.WriteLine("ERROR: --generate-password and --password are mutually exclusive.");
            PrintUsage();
            errorExitCode = 5;
            return null;
        }

        // --- Validate: --password value length ---
        if (plainPassword is not null && plainPassword.Length < MinPasswordLength)
        {
            Console.Error.WriteLine(
                $"ERROR: --password value is {plainPassword.Length} characters; minimum is {MinPasswordLength}.");
            PrintUsage();
            errorExitCode = 5;
            return null;
        }

        return new Args(email, plainPassword, generatePw, admin);
    }

    /// <summary>Prints the canonical usage block to <c>Console.Error</c>.</summary>
    private static void PrintUsage()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "Usage: dotnet run --project ProjectCeres -- --seed-dev-user --email <addr> [--generate-password | --password <pw>]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  --email <addr>           Email for the user account (required)");
        Console.Error.WriteLine("  --generate-password      Generate a 32-char strong password and print it once");
        Console.Error.WriteLine($"  --password <pw>          Use the provided password (must be >={MinPasswordLength} chars; appears in process args)");
        Console.Error.WriteLine("  --admin                  Grant the Admin role to this account; permitted outside Development only when no admin exists yet.");
        Console.Error.WriteLine("  --help                   Show this message");
        Console.Error.WriteLine();
    }

    /// <summary>
    /// Counts sentinel-tagged rows across all 16 user-owned tables using the admin
    /// connection (BYPASSRLS). Used for both the pre-check and the post-check.
    /// </summary>
    private static async Task<long> CountSentinelRowsAsync(AdminDbContext adminDb, string sentinelStr)
    {
        long total = 0;
        var allowedTables = UserOwnedModel.FinanceTables(adminDb.Model)
            .Select(t => t.PostgresTableName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var table in UserOwnedModel.FinanceTables(adminDb.Model).Select(t => t.PostgresTableName))
        {
            AssertKnownTable(table, allowedTables);
            // EF cannot parameterize an identifier and has no scalar-returning ExecuteSql;
            // table is guarded above, sentinel is a constant — raw Npgsql is correct here. (9.1.6.a)
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
