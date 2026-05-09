using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class FailedLoginRecorderTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public FailedLoginRecorderTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted!.EndsWith("@recorder-test.local"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task BadCredentials_WritesOneRow()
    {
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "user@recorder-test.local",
            userId: Guid.NewGuid(),
            FailedLoginReason.BadCredentials,
            "203.0.113.5",
            "ua/1.0",
            CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "user@recorder-test.local")
            .ToListAsync();

        rows.Should().HaveCount(1);
        rows[0].Reason.Should().Be(FailedLoginReason.BadCredentials);
        rows[0].IpAddress.Should().Be("203.0.113.5");
        rows[0].UserAgent.Should().Be("ua/1.0");
    }
}
