using FluentAssertions;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Stage 7.6.7 / ADR-0073 — pins the contract of the discriminated union.
/// </summary>
public class UserContextTests
{
    [Fact]
    public void Resolved_carries_the_user_id_through_pattern_match()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        UserContext sut = new UserContext.Resolved(id);

        var extracted = sut switch
        {
            UserContext.Resolved r => r.UserId,
            _ => Guid.Empty,
        };

        extracted.Should().Be(id);
    }

    [Fact]
    public void PreAuth_and_Background_carry_their_payloads_through_pattern_match()
    {
        UserContext preAuth = new UserContext.PreAuth("Auth.Login");
        UserContext background = new UserContext.Background("Hangfire job did not enter scope");

        var preAuthName = preAuth switch { UserContext.PreAuth p => p.CallSite, _ => "" };
        var bgReason = background switch { UserContext.Background b => b.Reason, _ => "" };

        preAuthName.Should().Be("Auth.Login");
        bgReason.Should().Be("Hangfire job did not enter scope");
    }

    [Fact]
    public void Uninitialized_is_a_singleton_via_Instance()
    {
        // Different references would still be record-equal (records compare by value),
        // but using Instance avoids the allocation churn for the case that fires per
        // EF model-creation invocation.
        var a = UserContext.Uninitialized.Instance;
        var b = UserContext.Uninitialized.Instance;

        ReferenceEquals(a, b).Should().BeTrue();
    }

    [Fact]
    public void Records_compare_by_value_so_two_Resolved_with_same_id_are_equal()
    {
        var id = Guid.NewGuid();
        var a = new UserContext.Resolved(id);
        var b = new UserContext.Resolved(id);

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
    }
}
