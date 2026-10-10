using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Gdpr;

/// <summary>
/// Stage 13.b schema foundation — pins that ApplicationUser exposes LastLoginAt
/// (non-null) and InactivityWarningSentAt (nullable), and that both round-trip
/// through AdminDbContext (BYPASSRLS) unchanged.
/// </summary>
[Collection("IntegrationParallel1")]
public class InactivityColumnsTests : IntegrationTestBase<Bucket1Factory>
{
    public InactivityColumnsTests(Bucket1Factory factory, Bucket1Database bucketDb) : base(factory, bucketDb) { }

    [Fact]
    public async Task ApplicationUser_exposes_LastLoginAt_and_InactivityWarningSentAt()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var marker = $"col-test-{Guid.NewGuid():N}@col-test.invalid";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = marker,
            UserName = marker,
            LastLoginAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var read = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(u => u.Email == marker);
        read.LastLoginAt.Should().Be(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        read.InactivityWarningSentAt.Should().BeNull();

        await db.Users.IgnoreQueryFilters().Where(u => u.Email == marker).ExecuteDeleteAsync();
    }
}
