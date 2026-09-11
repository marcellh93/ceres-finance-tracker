using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 7.5 / ADR-0068 Group 4: backstop properties.
///
/// <list type="bullet">
///   <item>The interceptor logs a warning when a database connection opens under
///         <c>Guid.Empty</c> outside a tagged pre-auth call site.</item>
///   <item>Pre-auth call sites do NOT log a warning.</item>
///   <item>The GUC does not leak across Npgsql-pooled connections — Npgsql's
///         default <c>DISCARD ALL</c> reset clears it, and the interceptor's
///         <c>RESET</c> on Guid.Empty is belt-and-braces. Verified by acquiring a
///         pooled connection, setting the GUC, releasing, re-acquiring, and
///         asserting the policy evaluates to zero rows.</item>
/// </list>
/// </summary>
[Collection("RlsTests")]
public class Group4_BackstopTests
{
    private readonly RlsTestFixture _fixture;

    public Group4_BackstopTests(RlsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Interceptor_logs_Error_when_context_is_Background()
    {
        // Stage 7.6.7 / ADR-0073: replaces the old Guid.Empty-without-tagger contract.
        // A request that reaches the DB without auth and without a [PreAuthCallSite] tag
        // surfaces as UserContext.Background, which is an Error (not a Warning) — the
        // case is always a bug worth investigating.
        var logs = new List<string>();
        var interceptor = MakeInterceptor(new UserContext.Background("untagged endpoint"), logs);

        await using var ctx = BuildContextWithInterceptor(Guid.Empty, interceptor);
        await ctx.AccountTypes.AnyAsync();

        logs.Should().Contain(l => l.Contains("Error:") && l.Contains("Background"));
    }

    [Fact]
    public async Task Interceptor_does_not_log_Warning_or_Error_on_PreAuth_path()
    {
        var logs = new List<string>();
        var interceptor = MakeInterceptor(new UserContext.PreAuth("Auth.Login"), logs);

        await using var ctx = BuildContextWithInterceptor(Guid.Empty, interceptor);
        await ctx.AccountTypes.AnyAsync();

        logs.Should().NotContain(l => l.Contains("Warning:") || l.Contains("Error:"));
    }

    [Fact]
    public async Task GUC_does_not_leak_across_pooled_connections()
    {
        // Seed: user A owns one Account.
        var accountA = new Account
        {
            Id            = Guid.NewGuid(),
            UserId        = _fixture.UserA,
            Name          = $"Pool leak test {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true,
        };
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Accounts.Add(accountA);
            await admin.SaveChangesAsync();
        }

        try
        {
            // First connection: set the GUC to user A, run a query, close. The connection
            // returns to Npgsql's pool — DISCARD ALL should reset the GUC.
            await using (var connA = await _fixture.OpenAppConnectionAsync(_fixture.UserA))
            {
                await using var cmd = connA.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM \"Accounts\"";
                var visibleToA = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
                visibleToA.Should().BeGreaterThan(0, "user A should see at least their own row");
            }

            // Second connection: do NOT set the GUC. If the pool returned the same physical
            // connection without DISCARD ALL, the GUC would still hold user A's value and
            // we'd see A's rows. With DISCARD ALL the GUC is reset to empty string; the
            // policy's NULLIF collapses that to NULL; the count is 0.
            await using var connB = new NpgsqlConnection(RlsTestFixture.AppConnectionString);
            await connB.OpenAsync();
            await using var cmdB = connB.CreateCommand();
            cmdB.CommandText = "SELECT COUNT(*) FROM \"Accounts\"";
            var visibleWithoutGuc = (long)(await cmdB.ExecuteScalarAsync() ?? 0L);
            visibleWithoutGuc.Should().Be(0,
                "after pool return + reset, the GUC must be cleared so the policy filters every row");
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.Accounts.IgnoreQueryFilters()
                .Where(a => a.Id == accountA.Id)
                .ExecuteDeleteAsync();
        }
    }

    // -------------- helpers --------------

    private static RowLevelSecurityInterceptor MakeInterceptor(UserContext context, List<string> logs)
    {
        var accessor = new FakeCurrentUserAccessor(context);

        var loggerFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            b.AddProvider(new InMemoryLoggerProvider(logs));
        });
        var logger = loggerFactory.CreateLogger<RowLevelSecurityInterceptor>();

        return new RowLevelSecurityInterceptor(accessor, logger);
    }

    private static AppDbContext BuildContextWithInterceptor(Guid userId, RowLevelSecurityInterceptor interceptor)
    {
        var accessor = new FakeCurrentUserAccessor(userId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(RlsTestFixture.AppConnectionString)
            .AddInterceptors(new UserOwnershipInterceptor(accessor))
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options, accessor);
    }
}
