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
}
