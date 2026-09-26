using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common.Exceptions;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 13.9 Task 1 — ErasureRequests is a real migrated table with a user_isolation
/// policy (AddErasureRequest migration), so this follows the same Group 1 FromSqlRaw-bypass
/// pattern as ExportJobs: no throwaway self-bootstrapped table. Also covers the USING
/// positive control and the WITH CHECK negative control (Group 2 INSERT-foreign-UserId
/// pattern), so both halves of the policy are pinned.
/// </summary>
[Collection("RlsTests")]
public class ErasureRequestRlsTests
{
    private readonly RlsTestFixture _fixture;

    public ErasureRequestRlsTests(RlsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ErasureRequest_is_invisible_across_users_under_rls()
    {
        var requestA = NewErasureRequest(_fixture.UserA);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.ErasureRequests.Add(requestA);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appB = _fixture.CreateAppContext(_fixture.UserB);
            var rowsVisibleToB = await appB.ErasureRequests
                .FromSqlRaw("SELECT * FROM \"ErasureRequests\"")
                .ToListAsync();

            rowsVisibleToB.Should().BeEmpty(
                "user B must not see user A's ErasureRequest under RLS");
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.ErasureRequests
                .IgnoreQueryFilters()
                .Where(r => r.Id == requestA.Id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ErasureRequest_is_visible_to_its_own_owner_under_rls()
    {
        // Positive control: the policy must not simply hide every row — user A
        // scoped to their own GUC must still see their own ErasureRequest.
        var requestA = NewErasureRequest(_fixture.UserA);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.ErasureRequests.Add(requestA);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appA = _fixture.CreateAppContext(_fixture.UserA);
            var rowsVisibleToA = await appA.ErasureRequests
                .FromSqlRaw("SELECT * FROM \"ErasureRequests\"")
                .ToListAsync();

            rowsVisibleToA.Should().ContainSingle()
                .Which.Id.Should().Be(requestA.Id);
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.ErasureRequests
                .IgnoreQueryFilters()
                .Where(r => r.Id == requestA.Id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task INSERT_ErasureRequest_with_foreign_UserId_raises_RlsPolicyViolation()
    {
        // WITH CHECK negative control: user B cannot stamp an ErasureRequest with A's
        // UserId — the cancel-token path must never be able to mint a request that
        // resolves to someone else's account.
        await using var appB = _fixture.CreateAppContext(_fixture.UserB);
        appB.ErasureRequests.Add(new ErasureRequest
        {
            Id                = Guid.NewGuid(),
            UserId            = _fixture.UserA,            // foreign
            Status            = ErasureStatus.Sealed,
            RequestedAt       = DateTime.UtcNow,
            ExecuteAfter      = DateTime.UtcNow.AddHours(72),
            CancelTokenLookup = Array.Empty<byte>(),
        });

        var act = async () => await appB.SaveChangesAsync();

        var ex = await act.Should().ThrowAsync<RlsPolicyViolationException>();
        ex.Which.TableName.Should().Be("ErasureRequests");
        ex.Which.GucUserId.Should().Be(_fixture.UserB);
        ex.Which.OriginalException.SqlState.Should().Be("42501");
    }

    private static ErasureRequest NewErasureRequest(Guid userId) => new()
    {
        Id                = Guid.NewGuid(),
        UserId            = userId,
        Status            = ErasureStatus.Sealed,
        RequestedAt       = DateTime.UtcNow,
        ExecuteAfter      = DateTime.UtcNow.AddHours(72),
        CancelTokenLookup = Array.Empty<byte>(),
    };
}
