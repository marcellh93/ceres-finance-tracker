using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Pins AuthTestFixture.PurgeUsersByEmailSuffixAsync, which every auth test class runs at setup so
/// a user left by an interrupted run cannot fail the next run's registration.
/// </summary>
[Collection("IntegrationParallel4")]
public class AuthTestFixturePurgeTests : IntegrationTestBase<Bucket4AuthFactory>, IAsyncLifetime
{
    private const string SuffixA = "@purge-a-test.local";
    private const string SuffixB = "@purge-b-test.local";
    private const string SuffixKeep = "@purge-keep-test.local";
    private readonly Bucket4AuthFactory _factory;

    public AuthTestFixturePurgeTests(Bucket4AuthFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, SuffixA, SuffixB, SuffixKeep);
    public Task DisposeAsync() => AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, SuffixA, SuffixB, SuffixKeep);

    private async Task<int> CountUsersAsync(string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await um.Users.CountAsync(u => u.Email!.EndsWith(suffix));
    }

    [Fact]
    public async Task Purge_removes_a_stale_user_and_the_rows_it_owns()
    {
        var stale = await AuthTestFixture.RegisterUserAsync(_factory, $"stale{SuffixA}");

        await AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, SuffixA);

        (await CountUsersAsync(SuffixA)).Should().Be(0);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == stale.Id))
            .Should().Be(0, "registering seeds categories, and deleting the user alone leaves them behind");
    }

    [Fact]
    public async Task Purge_lets_the_same_email_register_again()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, $"again{SuffixA}");

        await AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, SuffixA);

        var act = () => AuthTestFixture.RegisterUserAsync(_factory, $"again{SuffixA}");
        await act.Should().NotThrowAsync("this is the exact failure a stale row used to cause");
    }

    [Fact]
    public async Task Purge_handles_several_suffixes_in_one_call()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, $"one{SuffixA}");
        await AuthTestFixture.RegisterUserAsync(_factory, $"two{SuffixB}");

        await AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, SuffixA, SuffixB);

        (await CountUsersAsync(SuffixA)).Should().Be(0);
        (await CountUsersAsync(SuffixB)).Should().Be(0);
    }

    [Fact]
    public async Task Purge_leaves_users_with_other_suffixes_alone()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, $"gone{SuffixA}");
        await AuthTestFixture.RegisterUserAsync(_factory, $"keep{SuffixKeep}");

        await AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, SuffixA);

        (await CountUsersAsync(SuffixKeep)).Should().Be(1, "only the named suffixes may be purged");
    }

    [Fact]
    public async Task Purge_is_a_no_op_when_there_is_nothing_to_remove()
    {
        var act = () => AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, SuffixA);

        await act.Should().NotThrowAsync();
    }
}
