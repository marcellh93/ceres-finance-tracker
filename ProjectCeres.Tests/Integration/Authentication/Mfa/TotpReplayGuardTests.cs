using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationTests")]
public class TotpReplayGuardTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public TotpReplayGuardTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.TotpReplayEntries
            .Where(e => e.UserId.ToString().StartsWith("ddddeeee-"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TryAcceptAsync_accepts_first_use_and_rejects_replay_within_window()
    {
        var userId = new Guid("ddddeeee-0000-0000-0000-000000000001");
        using var scope = _factory.Services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<TotpReplayGuard>();

        (await guard.TryAcceptAsync(userId, "123456", CancellationToken.None)).Should().BeTrue();
        (await guard.TryAcceptAsync(userId, "123456", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task TryAcceptAsync_does_not_block_a_different_user_using_the_same_numeric_code()
    {
        var userA = new Guid("ddddeeee-0000-0000-0000-000000000002");
        var userB = new Guid("ddddeeee-0000-0000-0000-000000000003");
        using var scope = _factory.Services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<TotpReplayGuard>();

        (await guard.TryAcceptAsync(userA, "654321", CancellationToken.None)).Should().BeTrue();
        (await guard.TryAcceptAsync(userB, "654321", CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task TryAcceptAsync_purges_rows_older_than_replay_window()
    {
        var userId = new Guid("ddddeeee-0000-0000-0000-000000000004");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<TotpReplayGuard>();

        // Seed an "old" entry just past the replay window.
        db.TotpReplayEntries.Add(new TotpReplayEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = "$argon2id$v=19$m=19456,t=2,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            AcceptedAt = DateTime.UtcNow - TimeSpan.FromMinutes(5),
        });
        await db.SaveChangesAsync();

        // Accept any new code → should trigger purge.
        await guard.TryAcceptAsync(userId, "111111", CancellationToken.None);

        var remaining = await db.TotpReplayEntries
            .Where(e => e.UserId == userId)
            .ToListAsync();
        // One row remains (the one we just accepted); the old one was purged.
        remaining.Should().HaveCount(1);
        remaining[0].AcceptedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }
}
