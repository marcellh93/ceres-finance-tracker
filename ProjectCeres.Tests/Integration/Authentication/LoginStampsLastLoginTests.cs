using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Stage 13.b: a successful login resets the inactivity clock and clears any pending warning.
/// </summary>
[Collection("IntegrationParallel4")]
public class LoginStampsLastLoginTests : IntegrationTestBase<Bucket4AuthFactory>, IAsyncLifetime
{
    private const string Suffix = "@lastlogin-test.invalid";
    private readonly Bucket4AuthFactory _factory;

    public LoginStampsLastLoginTests(Bucket4AuthFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, Suffix);

    public Task DisposeAsync() => AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, Suffix);

    [Fact]
    public async Task Successful_login_stamps_LastLoginAt_and_clears_warning()
    {
        var email = "stamp-login" + Suffix;
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.IgnoreQueryFilters().Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LastLoginAt, now.AddDays(-400))
                    .SetProperty(u => u.InactivityWarningSentAt, (DateTime?)now.AddDays(-100)));
        }

        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var readScope = _factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var read = await readDb.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Email == email);
        read.LastLoginAt.Should().BeCloseTo(now, TimeSpan.FromMinutes(1));
        read.InactivityWarningSentAt.Should().BeNull();
    }
}
