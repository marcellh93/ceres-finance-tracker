using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Common;

public class BackgroundJobScopeTests
{
    [Fact]
    public async Task RunAsync_refuses_when_userId_is_Guid_Empty_before_work_runs()
    {
        var scope = new UserScope();
        var logs = new List<string>();
        using var lf = LoggerFactory.Create(b => b.AddProvider(new InMemoryLoggerProvider(logs)));
        var sut = new BackgroundJobScope(scope, lf.CreateLogger<BackgroundJobScope>());

        var workInvoked = false;
        Func<Task> act = () => sut.RunAsync(Guid.Empty, "TestJob", () => { workInvoked = true; return Task.CompletedTask; });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Background jobs must declare a user*");
        workInvoked.Should().BeFalse("work must not run when the doorway refuses");
        logs.Should().Contain(l => l.Contains("Error") && l.Contains("Background job refused") && l.Contains("TestJob"));
    }

    [Fact]
    public async Task RunAsync_invokes_work_under_EnterAs_when_userId_is_valid()
    {
        var scope = new UserScope();
        var sut = new BackgroundJobScope(scope, NullLogger<BackgroundJobScope>.Instance);
        var userId = Guid.NewGuid();

        Guid? observed = null;
        await sut.RunAsync(userId, "TestJob", () =>
        {
            observed = scope.Current;
            return Task.CompletedTask;
        });

        observed.Should().Be(userId);
    }

    [Fact]
    public async Task RunAsync_disposes_scope_after_work_completes()
    {
        var scope = new UserScope();
        var sut = new BackgroundJobScope(scope, NullLogger<BackgroundJobScope>.Instance);

        await sut.RunAsync(Guid.NewGuid(), "TestJob", () => Task.CompletedTask);

        scope.Current.Should().BeNull("scope unwinds after work completes");
    }

    [Fact]
    public async Task RunAsync_disposes_scope_when_work_throws()
    {
        var scope = new UserScope();
        var sut = new BackgroundJobScope(scope, NullLogger<BackgroundJobScope>.Instance);

        Func<Task> act = () => sut.RunAsync(Guid.NewGuid(), "TestJob", () =>
            throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        scope.Current.Should().BeNull("scope unwinds even when work throws");
    }

    [Fact]
    public async Task RunAsync_nests_correctly()
    {
        var scope = new UserScope();
        var sut = new BackgroundJobScope(scope, NullLogger<BackgroundJobScope>.Instance);
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();

        Guid? observedInner = null;
        Guid? observedAfterInner = null;
        await sut.RunAsync(outer, "Outer", async () =>
        {
            await sut.RunAsync(inner, "Inner", () =>
            {
                observedInner = scope.Current;
                return Task.CompletedTask;
            });
            observedAfterInner = scope.Current;
        });

        observedInner.Should().Be(inner);
        observedAfterInner.Should().Be(outer, "inner scope unwinds back to outer");
        scope.Current.Should().BeNull();
    }
}
