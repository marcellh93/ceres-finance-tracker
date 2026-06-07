using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

/// <summary>
/// Stage 9.5d Task 10 — the LAST flow, a verify-or-discover test. Lockout-unlock confirm is
/// PRE-AUTH (/api/auth/lockout-unlock is AllowAnonymous + [PreAuthCallSite]); the raw token
/// grants authority. LockoutUnlockService.ConfirmAsync is [RlsBypassJustified("CER-1007")] and
/// uses IgnoreQueryFilters() for its reads — but IgnoreQueryFilters strips only EF's query
/// filter, NOT Postgres RLS. The candidate lookup, the in-lock re-read, and the consume
/// ExecuteUpdateExactlyAsync all run on _db (ceres_app), and ConfirmAsync opens NO
/// BeginPreAuthUserScopeAsync (unlike IssueAsync, which does). This is structurally the SAME
/// shape as the email-change gap Task 9 found before it was fixed (admin lookup + PreAuthUserScope).
/// This test genuinely discovers whether the confirm flow survives under ceres_app: a 204 means
/// the GUC is already covered (test-only); a 401/500 on the token lookup or consume-write is a
/// latent production gap to escalate, NOT to fix here.
/// </summary>
[Collection("AppRoleTests")]
public class LockoutUnlockUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public LockoutUnlockUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task LockoutUnlock_confirm_under_ceres_app_consumes_token_and_clears_lockout()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1: seed a confirmed user. Register via the production HTTP endpoint (it owns its
        // PreAuthUserScope, so the category seed passes RLS under ceres_app — RegisterUserAsync
        // would itself trip 42501 on Categories, the Task 4 trap), then confirm the email via the
        // BYPASSRLS admin context.
        var email = $"lockout-{Marker}@approle-test.local";
        var register = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/register", new { email, password = AuthTestFixture.ValidPassword });
        register.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var admin = Factory.NewAdminContext())
        {
            var seeded = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
            _userId = seeded.Id;
            seeded.EmailConfirmed = true;
            // Lock the user out: set LockoutEnd in the future + AccessFailedCount so ConfirmAsync
            // has lock state to clear. AspNetUsers is not RLS-bound, so the admin write suffices.
            seeded.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
            seeded.AccessFailedCount = 5;
            await admin.Context.SaveChangesAsync();
        }

        // Step 2: obtain the raw unlock token. IssueAsync runs only on the 10-failed-login lockout
        // transition; admin-seed a token directly (mirrors LockoutUnlockConfirmTests.Arrange...),
        // writing the row via the BYPASSRLS admin context. The lookup hash + token hash come from
        // the same DI singletons the production confirm path uses.
        string rawToken;
        using (var scope = Factory.Services.CreateScope())
        {
            var generator = scope.ServiceProvider.GetRequiredService<LockoutUnlockTokenGenerator>();
            var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
            rawToken = generator.Generate();
            var now = DateTime.UtcNow;
            await using var admin = Factory.NewAdminContext();
            admin.Context.LockoutUnlockTokens.Add(new LockoutUnlockToken
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                TokenLookup = lookupHasher.ComputeLookup(rawToken),
                TokenHash = generator.Hash(rawToken),
                CreatedAt = now,
                ExpiresAt = now + LockoutUnlockService.TokenLifetime,
                ConsumedAt = null,
            });
            await admin.Context.SaveChangesAsync();
        }

        // Step 3: confirm under ceres_app (anonymous, token). A 401 here means the candidate
        // lookup returned zero rows because RLS filtered them with app.current_user_ref unset
        // (ConfirmAsync uses _db + IgnoreQueryFilters with no BeginPreAuthUserScopeAsync), or a
        // 500 means the consume-write hit RLS 42501 — the latent gap. A non-204 is the escalation
        // signal (do NOT fix here).
        var confirmResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/lockout-unlock", new { token = rawToken });
        confirmResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "lockout-unlock confirm must persist under ceres_app");

        // Step 4: the token is consumed, seen via the owner's ceres_app context; and the user's
        // lockout is cleared (asserted via admin context — AspNetUsers is not RLS-bound).
        await using (var app = Factory.NewAppContext(_userId))
        {
            var consumed = await app.Context.LockoutUnlockTokens
                .CountAsync(t => t.UserId == _userId && t.ConsumedAt != null);
            consumed.Should().BeGreaterThanOrEqualTo(1, "the confirm flow must stamp ConsumedAt on the token row");
        }

        await using (var admin = Factory.NewAdminContext())
        {
            var cleared = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == _userId);
            cleared.LockoutEnd.Should().BeNull("confirm must clear the user's LockoutEnd");
            cleared.AccessFailedCount.Should().Be(0, "confirm must reset AccessFailedCount");
        }

        // Step 5: positive + negative RLS control — exactly one token row for this user, visible
        // only to the owner under ceres_app. One issuance writes one row, and the confirm flow
        // consumes (updates) that same row rather than adding another.
        await AssertRlsVisibility<LockoutUnlockToken>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: t => t.UserId == _userId, expectedOwnerCount: 1);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.LockoutUnlockTokens.IgnoreQueryFilters().Where(t => t.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Categories.IgnoreQueryFilters().Where(c => c.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
