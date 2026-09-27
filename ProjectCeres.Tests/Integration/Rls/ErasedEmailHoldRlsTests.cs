using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common.Exceptions;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 13.9 Task 6b — ErasedEmailHolds is a real migrated table with a
/// user_isolation policy (AddErasedEmailHold migration), same Group 1/2 pattern
/// as ErasureRequestRlsTests.
/// </summary>
[Collection("RlsTests")]
public class ErasedEmailHoldRlsTests
{
    private readonly RlsTestFixture _fixture;

    public ErasedEmailHoldRlsTests(RlsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ErasedEmailHold_is_invisible_across_users_under_rls()
    {
        var holdA = NewHold(_fixture.UserA);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.ErasedEmailHolds.Add(holdA);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appB = _fixture.CreateAppContext(_fixture.UserB);
            var rowsVisibleToB = await appB.ErasedEmailHolds
                .FromSqlRaw("SELECT * FROM \"ErasedEmailHolds\"")
                .ToListAsync();

            rowsVisibleToB.Should().BeEmpty(
                "user B must not see user A's ErasedEmailHold under RLS");
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.ErasedEmailHolds
                .IgnoreQueryFilters()
                .Where(h => h.Id == holdA.Id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ErasedEmailHold_is_visible_to_its_own_owner_under_rls()
    {
        var holdA = NewHold(_fixture.UserA);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.ErasedEmailHolds.Add(holdA);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appA = _fixture.CreateAppContext(_fixture.UserA);
            var rowsVisibleToA = await appA.ErasedEmailHolds
                .FromSqlRaw("SELECT * FROM \"ErasedEmailHolds\"")
                .ToListAsync();

            rowsVisibleToA.Should().ContainSingle()
                .Which.Id.Should().Be(holdA.Id);
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.ErasedEmailHolds
                .IgnoreQueryFilters()
                .Where(h => h.Id == holdA.Id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task INSERT_ErasedEmailHold_with_foreign_UserId_raises_RlsPolicyViolation()
    {
        await using var appB = _fixture.CreateAppContext(_fixture.UserB);
        appB.ErasedEmailHolds.Add(new ErasedEmailHold
        {
            Id = Guid.NewGuid(),
            UserId = _fixture.UserA,     // foreign
            EmailFingerprint = new byte[32],
            ErasedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });

        var act = async () => await appB.SaveChangesAsync();

        var ex = await act.Should().ThrowAsync<RlsPolicyViolationException>();
        ex.Which.TableName.Should().Be("ErasedEmailHolds");
        ex.Which.GucUserId.Should().Be(_fixture.UserB);
        ex.Which.OriginalException.SqlState.Should().Be("42501");
    }

    private static ErasedEmailHold NewHold(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        EmailFingerprint = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray(),
        ErasedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(30),
    };
}
