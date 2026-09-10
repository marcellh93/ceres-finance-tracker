using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationParallel2")]
public class MfaBackupCodeServiceTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public MfaBackupCodeServiceTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserMfaBackupCodes
            .IgnoreQueryFilters()
            .Where(c => c.UserId.ToString().StartsWith("dddddddd-"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GenerateAndPersistAsync_yields_10_unique_argon2id_hashed_rows()
    {
        var userId = new Guid("dddddddd-0000-0000-0000-000000000001");
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var codes = await svc.GenerateAndPersistAsync(userId, CancellationToken.None);

        codes.Should().HaveCount(10);
        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => c.Length == 19); // 16 chars + 3 hyphens

        var rows = await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == userId).ToListAsync();
        rows.Should().HaveCount(10);
        rows.Should().OnlyContain(r => r.CodeHash.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$"));
        rows.Should().OnlyContain(r => r.UsedAt == null);
    }

    [Fact]
    public async Task VerifyAndConsumeAsync_marks_UsedAt_and_returns_true_on_match()
    {
        var userId = new Guid("dddddddd-0000-0000-0000-000000000002");
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var codes = await svc.GenerateAndPersistAsync(userId, CancellationToken.None);
        var first = codes.First();

        var ok = await svc.VerifyAndConsumeAsync(userId, first, "10.0.0.1", CancellationToken.None);
        ok.Should().BeTrue();

        var row = await db.UserMfaBackupCodes
            .IgnoreQueryFilters()
            .FirstAsync(c => c.UserId == userId && c.UsedAt != null);
        row.UsedFromIp.Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task VerifyAndConsumeAsync_returns_false_on_replay_of_used_code()
    {
        var userId = new Guid("dddddddd-0000-0000-0000-000000000003");
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();

        var codes = await svc.GenerateAndPersistAsync(userId, CancellationToken.None);
        var first = codes.First();

        (await svc.VerifyAndConsumeAsync(userId, first, "10.0.0.1", CancellationToken.None)).Should().BeTrue();
        (await svc.VerifyAndConsumeAsync(userId, first, "10.0.0.1", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAndConsumeAsync_accepts_hyphenated_or_unhyphenated_input()
    {
        var userId = new Guid("dddddddd-0000-0000-0000-000000000004");
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();

        var codes = await svc.GenerateAndPersistAsync(userId, CancellationToken.None);
        var first = codes.First();
        var unhyphenated = first.Replace("-", "");

        var ok = await svc.VerifyAndConsumeAsync(userId, unhyphenated, "10.0.0.1", CancellationToken.None);
        ok.Should().BeTrue();
    }

    // ── Edge-case batch A (Stage 6b.3) ──────────────────────────────────────

    [Fact]
    public async Task VerifyAndConsumeAsync_RejectsInvalidCrockfordChars()
    {
        // Crockford base-32 excludes I, L, O, U (visually ambiguous).
        // NormalizeForVerify strips hyphens, uppercases, then tests BackupCodeShape.
        // Codes made entirely of excluded chars must return false (shape mismatch → null → false).
        var userId = new Guid("dddddddd-0000-0000-0000-000000000006");
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();

        // Seed valid backup codes so the user has rows (otherwise false would be trivially
        // correct for a missing-user reason, not the shape-rejection reason we're testing).
        await svc.GenerateAndPersistAsync(userId, CancellationToken.None);

        // I — excluded Crockford character
        var resultI = await svc.VerifyAndConsumeAsync(userId, "IIII-IIII-IIII-IIII", "10.0.0.1", CancellationToken.None);
        resultI.Should().BeFalse("'I' is not in the Crockford alphabet — shape must be rejected");

        // L — excluded Crockford character
        var resultL = await svc.VerifyAndConsumeAsync(userId, "LLLL-LLLL-LLLL-LLLL", "10.0.0.1", CancellationToken.None);
        resultL.Should().BeFalse("'L' is not in the Crockford alphabet — shape must be rejected");

        // O — excluded Crockford character
        var resultO = await svc.VerifyAndConsumeAsync(userId, "OOOO-OOOO-OOOO-OOOO", "10.0.0.1", CancellationToken.None);
        resultO.Should().BeFalse("'O' is not in the Crockford alphabet — shape must be rejected");

        // U — excluded Crockford character
        var resultU = await svc.VerifyAndConsumeAsync(userId, "UUUU-UUUU-UUUU-UUUU", "10.0.0.1", CancellationToken.None);
        resultU.Should().BeFalse("'U' is not in the Crockford alphabet — shape must be rejected");
    }

    [Fact]
    public async Task RegenerateAsync_invalidates_all_previous_codes_and_yields_10_new()
    {
        var userId = new Guid("dddddddd-0000-0000-0000-000000000005");
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var first = await svc.GenerateAndPersistAsync(userId, CancellationToken.None);
        await svc.RegenerateAsync(userId, CancellationToken.None);
        var second = await db.UserMfaBackupCodes
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId)
            .ToListAsync();

        second.Should().HaveCount(10);
        // None of the new rows should validate against the old plaintext codes.
        var oldFirst = first.First();
        var ok = await svc.VerifyAndConsumeAsync(userId, oldFirst, "10.0.0.1", CancellationToken.None);
        ok.Should().BeFalse();
    }
}
