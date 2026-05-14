using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Common.Exceptions;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Stage 7.6.7 / ADR-0073 — pins the contract of <c>ICurrentUserAccessor.Require()</c>:
/// returns <see cref="UserContext.Resolved"/> or throws
/// <see cref="UserContextRequiredException"/> with the actual case named in the message.
/// </summary>
public class CurrentUserAccessorExtensionsTests
{
    [Fact]
    public void Require_returns_the_Resolved_case_when_user_is_resolved()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var sut = new FakeCurrentUserAccessor(new UserContext.Resolved(id));

        var resolved = sut.Require();

        resolved.UserId.Should().Be(id);
    }

    [Fact]
    public void Require_throws_for_PreAuth_and_message_names_PreAuth()
    {
        var sut = new FakeCurrentUserAccessor(new UserContext.PreAuth("Auth.Login"));

        var act = sut.Require;

        act.Should().Throw<UserContextRequiredException>()
            .Which.Actual.Should().Be(nameof(UserContext.PreAuth));
    }

    [Fact]
    public void Require_throws_for_Background_and_message_names_Background()
    {
        var sut = new FakeCurrentUserAccessor(new UserContext.Background("untagged endpoint"));

        var act = sut.Require;

        act.Should().Throw<UserContextRequiredException>()
            .Which.Actual.Should().Be(nameof(UserContext.Background));
    }

    [Fact]
    public void Require_throws_for_Uninitialized_and_message_names_Uninitialized()
    {
        var sut = new FakeCurrentUserAccessor(UserContext.Uninitialized.Instance);

        var act = sut.Require;

        act.Should().Throw<UserContextRequiredException>()
            .Which.Actual.Should().Be(nameof(UserContext.Uninitialized));
    }

    [Fact]
    public void UserContextRequiredException_message_includes_both_expected_and_actual()
    {
        var sut = new FakeCurrentUserAccessor(UserContext.Uninitialized.Instance);

        var act = sut.Require;

        act.Should().Throw<UserContextRequiredException>()
            .Which.Message.Should().Contain(nameof(UserContext.Resolved))
            .And.Contain(nameof(UserContext.Uninitialized));
    }
}
