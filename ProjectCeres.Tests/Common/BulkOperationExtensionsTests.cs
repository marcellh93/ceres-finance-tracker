using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Common.Exceptions;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Stage 7.6.3 — unit tests for <see cref="BulkOperationExtensions"/>. Wraps
/// <c>ExecuteUpdateAsync</c> / <c>ExecuteDeleteAsync</c> with row-count enforcement so a
/// future "GUC didn't get set" bug fails loud instead of silently returning 0 rows. Tests
/// exercise the helper against <c>project_ceres_test</c> via <see cref="TestDbFixture"/>;
/// per <c>feedback_filter_test_queries_by_test_data</c> every query is filtered by a
/// per-test GUID-suffixed account name to avoid order-dependent reads in the shared
/// IntegrationTests xUnit collection.
/// </summary>
[Collection("IntegrationParallel3")]
public class BulkOperationExtensionsTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private string _marker = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _marker = $"BulkOpExt-{Guid.NewGuid():N}";
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<Account> SeedAccountAsync(string suffix = "")
    {
        var a = new Account
        {
            Id = Guid.NewGuid(),
            Name = $"{_marker}-{suffix}",
            AccountTypeId = 1,
            CurrencyId = 1,
            IsActive = true,
        };
        _fixture.Db.Accounts.Add(a);
        await _fixture.Db.SaveChangesAsync();
        return a;
    }

    // ------------------------------------------------------------------
    // ExecuteUpdateExactlyAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteUpdateExactlyAsync_returns_when_actual_count_matches_expected()
    {
        var a = await SeedAccountAsync();

        var act = async () => await _fixture.Db.Accounts
            .Where(x => x.Id == a.Id)
            .ExecuteUpdateExactlyAsync(s => s.SetProperty(x => x.IsActive, false));

        await act.Should().NotThrowAsync();
        var refreshed = await _fixture.Db.Accounts.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        refreshed.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteUpdateExactlyAsync_throws_when_actual_is_less_than_expected()
    {
        // The "GUC silently dropped" regression case: query matches zero rows because the
        // RLS policy filtered them out. Default expectedRows == 1, actual == 0 → throw.
        var nonExistent = Guid.NewGuid();

        var act = async () => await _fixture.Db.Accounts
            .Where(x => x.Id == nonExistent)
            .ExecuteUpdateExactlyAsync(s => s.SetProperty(x => x.IsActive, false));

        var ex = await act.Should().ThrowAsync<AffectedRowCountMismatchException>();
        ex.Which.Expected.Should().Be(1);
        ex.Which.Actual.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteUpdateExactlyAsync_throws_when_actual_is_greater_than_expected()
    {
        await SeedAccountAsync("a");
        await SeedAccountAsync("b");

        var act = async () => await _fixture.Db.Accounts
            .Where(x => x.Name.StartsWith(_marker))
            .ExecuteUpdateExactlyAsync(s => s.SetProperty(x => x.IsActive, false));

        var ex = await act.Should().ThrowAsync<AffectedRowCountMismatchException>();
        ex.Which.Expected.Should().Be(1);
        ex.Which.Actual.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteUpdateExactlyAsync_honours_custom_expectedRows()
    {
        await SeedAccountAsync("a");
        await SeedAccountAsync("b");
        await SeedAccountAsync("c");

        var act = async () => await _fixture.Db.Accounts
            .Where(x => x.Name.StartsWith(_marker))
            .ExecuteUpdateExactlyAsync(
                s => s.SetProperty(x => x.IsActive, false),
                expectedRows: 3);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteUpdateExactlyAsync_populates_CallSite_via_CallerMemberName()
    {
        // The CallSite field is auto-populated from [CallerMemberName] so log lines name
        // the calling method without the call site repeating itself. Failing assertion
        // reads "Expected … to be ExecuteUpdateExactlyAsync_populates_CallSite_via_CallerMemberName".
        var nonExistent = Guid.NewGuid();

        var act = async () => await _fixture.Db.Accounts
            .Where(x => x.Id == nonExistent)
            .ExecuteUpdateExactlyAsync(s => s.SetProperty(x => x.IsActive, false));

        var ex = await act.Should().ThrowAsync<AffectedRowCountMismatchException>();
        ex.Which.CallSite.Should().Be(nameof(ExecuteUpdateExactlyAsync_populates_CallSite_via_CallerMemberName));
    }

    [Fact]
    public async Task ExecuteUpdateExactlyAsync_message_includes_Expected_and_Actual()
    {
        await SeedAccountAsync("a");
        await SeedAccountAsync("b");

        var act = async () => await _fixture.Db.Accounts
            .Where(x => x.Name.StartsWith(_marker))
            .ExecuteUpdateExactlyAsync(s => s.SetProperty(x => x.IsActive, false));

        var ex = await act.Should().ThrowAsync<AffectedRowCountMismatchException>();
        ex.Which.Message.Should().Contain("expected 1");
        ex.Which.Message.Should().Contain("actual 2");
    }

    // ------------------------------------------------------------------
    // ExecuteDeleteExactlyAsync — coverage parity with the update helper
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteDeleteExactlyAsync_returns_when_actual_count_matches_expected()
    {
        var a = await SeedAccountAsync();

        var act = async () => await _fixture.Db.Accounts
            .Where(x => x.Id == a.Id)
            .ExecuteDeleteExactlyAsync();

        await act.Should().NotThrowAsync();
        (await _fixture.Db.Accounts.AnyAsync(x => x.Id == a.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteDeleteExactlyAsync_throws_when_actual_is_less_than_expected()
    {
        var nonExistent = Guid.NewGuid();

        var act = async () => await _fixture.Db.Accounts
            .Where(x => x.Id == nonExistent)
            .ExecuteDeleteExactlyAsync();

        var ex = await act.Should().ThrowAsync<AffectedRowCountMismatchException>();
        ex.Which.Expected.Should().Be(1);
        ex.Which.Actual.Should().Be(0);
    }
}
