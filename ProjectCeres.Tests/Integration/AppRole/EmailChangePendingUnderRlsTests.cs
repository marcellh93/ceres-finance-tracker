using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

/// <summary>
/// Stage 12.8. security-model.md § Email Address Change claims a foreign pending-change row is
/// "invisible, not merely unreported" — a claim about the DATABASE, not just the endpoint's
/// output. EmailChangePendingTests cannot establish it: it runs in the plain IntegrationTests
/// collection, which is wired to ceres_admin (BYPASSRLS), so it passes on GetPendingAsync's
/// explicit .Where(t =&gt; t.UserId == userId) alone. Delete the RLS policy and that test stays
/// green. This one runs under ceres_app with RLS live, so the claim is earned rather than
/// inferred. Raised by the 12.8 spec-intent review.
/// </summary>
[Collection("AppRoleTests")]
public class EmailChangePendingUnderRlsTests : AppRoleTestBase
{
    private Guid _ownerId;
    private Guid _otherId;

    public EmailChangePendingUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Pending_token_row_is_invisible_to_another_user_under_ceres_app()
    {
        _ownerId = Guid.NewGuid();
        _otherId = Guid.NewGuid();
        var newEmail = $"{Marker}-target@approle-test.local";

        // Seed via the admin context (BYPASSRLS) — the setup discipline AppRoleTestBase
        // documents: cross-user writes must not themselves be subject to the wall under test.
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.Users.Add(new ApplicationUser
            {
                Id = _ownerId,
                UserName = $"{Marker}-owner@approle-test.local",
                Email = $"{Marker}-owner@approle-test.local",
                NormalizedEmail = $"{Marker}-owner@approle-test.local".ToUpperInvariant(),
                NormalizedUserName = $"{Marker}-owner@approle-test.local".ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString(),
            });
            admin.Context.Users.Add(new ApplicationUser
            {
                Id = _otherId,
                UserName = $"{Marker}-other@approle-test.local",
                Email = $"{Marker}-other@approle-test.local",
                NormalizedEmail = $"{Marker}-other@approle-test.local".ToUpperInvariant(),
                NormalizedUserName = $"{Marker}-other@approle-test.local".ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString(),
            });
            admin.Context.EmailChangeTokens.Add(new EmailChangeToken
            {
                Id = Guid.NewGuid(),
                UserId = _ownerId,
                Purpose = EmailChangeTokenPurpose.VerifyNew,
                NewEmail = newEmail,
                TokenLookup = Guid.NewGuid().ToByteArray(),
                TokenHash = "unused-by-this-read-path",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                ConsumedAt = null,
            });
            await admin.Context.SaveChangesAsync();
        }

        // Positive + negative control under the RLS-active role: the owner's ceres_app context
        // sees exactly its own row; a different user's context sees zero. The negative half is
        // what "invisible" means — Postgres refuses the row, not just the application predicate.
        await AssertRlsVisibility<EmailChangeToken>(
            owner: _ownerId,
            otherUser: _otherId,
            predicate: t => t.NewEmail == newEmail,
            expectedOwnerCount: 1);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.EmailChangeTokens.IgnoreQueryFilters()
            .Where(t => t.NewEmail.Contains(Marker)).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters()
            .Where(u => u.Id == _ownerId || u.Id == _otherId).ExecuteDeleteAsync();
    }
}
