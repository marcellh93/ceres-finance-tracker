using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Shared cleanup helper for token tables that grow unbounded across test runs.
/// PasswordResetTokens and EmailChangeTokens both store Argon2id-hashed tokens
/// in single tables that the corresponding services scan via candidate-loop —
/// every unconsumed row in the table incurs an Argon2id verify on each
/// /confirm or /revoke call (production O(N) Argon2id-per-request DoS vector
/// tracked in planning-phase3.md § Stage 6c follow-ups).
///
/// Under the IntegrationTests xUnit collection (which serialises all integration
/// tests across one shared project_ceres_test database), accumulated rows from
/// prior test runs make later auth tests slow to the point of failing under
/// the 60s rate-limit window. Each test class that produces token rows
/// MUST call <see cref="DeleteAllTestTokensAsync"/> in its DisposeAsync.
/// </summary>
public static class AuthTestTokenCleanup
{
    /// <summary>
    /// Deletes every PasswordResetToken and EmailChangeToken row belonging to
    /// any user whose email ends with "@example.com" (the convention used by
    /// the EmailChange and most PasswordReset test classes). Idempotent and
    /// safe to call from DisposeAsync.
    /// </summary>
    public static async Task DeleteAllTestTokensAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userIds = um.Users
            .Where(u => u.Email!.EndsWith("@example.com"))
            .Select(u => u.Id)
            .ToList();
        if (userIds.Count == 0) return;

        await db.PasswordResetTokens
            .Where(t => userIds.Contains(t.UserId))
            .ExecuteDeleteAsync();
        await db.EmailChangeTokens
            .Where(t => userIds.Contains(t.UserId))
            .ExecuteDeleteAsync();
    }
}
