using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationParallel4")]
public class MfaBackupCodeRaceTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public MfaBackupCodeRaceTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@bc-race-test.local")).ToList())
        {
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task VerifyAndConsumeAsync_ConcurrentSubmissionsOfSameCode_OnlyOneSucceeds()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "race@bc-race-test.local");
        IReadOnlyList<string> codes;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            codes = await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }
        var code = codes[0];

        // Fire two parallel verify-and-consume calls in DIFFERENT scopes (each with its own DbContext)
        // so we exercise the actual concurrency boundary, not in-context tracking.
        async Task<bool> AttemptAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            return await bc.VerifyAndConsumeAsync(user.Id, code, "1.2.3.4", CancellationToken.None);
        }

        var t1 = AttemptAsync();
        var t2 = AttemptAsync();
        var results = await Task.WhenAll(t1, t2);

        results.Count(r => r).Should().Be(1, "only one parallel call should consume the same backup code");
        results.Count(r => !r).Should().Be(1);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var consumed = await verifyDb.UserMfaBackupCodes
            .IgnoreQueryFilters()
            .Where(c => c.UserId == user.Id && c.UsedAt != null)
            .CountAsync();
        consumed.Should().Be(1);
    }
}
