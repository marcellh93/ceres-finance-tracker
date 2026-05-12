using FluentAssertions;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Common;

public class UserScopeTests
{
    [Fact]
    public void EnterAs_sets_Current_for_duration_of_using_block()
    {
        IUserScope scope = new UserScope();
        var userId = Guid.NewGuid();

        scope.Current.Should().BeNull();
        using (scope.EnterAs(userId))
        {
            scope.Current.Should().Be(userId);
        }
        scope.Current.Should().BeNull();
    }

    [Fact]
    public void EnterAs_nests_with_stack_semantics()
    {
        IUserScope scope = new UserScope();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using (scope.EnterAs(a))
        {
            scope.Current.Should().Be(a);
            using (scope.EnterAs(b))
            {
                scope.Current.Should().Be(b);
            }
            scope.Current.Should().Be(a, "disposing inner scope restores outer");
        }
        scope.Current.Should().BeNull();
    }

    [Fact]
    public async Task EnterAs_propagates_across_await_boundaries()
    {
        IUserScope scope = new UserScope();
        var userId = Guid.NewGuid();

        using (scope.EnterAs(userId))
        {
            await Task.Yield();
            scope.Current.Should().Be(userId);
            await Task.Delay(1);
            scope.Current.Should().Be(userId);
        }
    }

    [Fact]
    public void Dispose_called_twice_is_a_no_op_and_does_not_restore_previous_again()
    {
        // Pins ScopeReleaser.Dispose's idempotency guard (`if (_disposed) return;`).
        // Without the guard, a second Dispose call would restore `previous` a second
        // time — which, if a nested scope had since entered, would clobber the
        // current value back to a stale captured `previous`.
        IUserScope scope = new UserScope();
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();

        var outerReleaser = scope.EnterAs(outer);
        scope.Current.Should().Be(outer);

        outerReleaser.Dispose();
        scope.Current.Should().BeNull("first Dispose restored the pre-outer value (null)");

        // Now enter a nested scope and call Dispose on the already-disposed outer
        // releaser. The guard must prevent it from clobbering the inner value.
        using (scope.EnterAs(inner))
        {
            scope.Current.Should().Be(inner);
            outerReleaser.Dispose();  // second dispose
            scope.Current.Should().Be(inner, "second Dispose on an already-disposed releaser must be a no-op");
        }
    }
}
