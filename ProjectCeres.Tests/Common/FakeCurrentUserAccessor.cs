using ProjectCeres.Common;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Test double for <see cref="ICurrentUserAccessor"/>. Replaces the soon-to-be-deleted
/// <c>SingleUserAccessor</c> at every <c>new SingleUserAccessor()</c> site in
/// <c>ProjectCeres.Tests/</c> (Stage 7 Task 18). Bind to a real test-fixture user id
/// so the accessor returns the user the surrounding service code actually expects;
/// unit-only tests that never read <c>UserId</c> can pass any <see cref="Guid"/>.
/// </summary>
public sealed class FakeCurrentUserAccessor(Guid userId) : ICurrentUserAccessor
{
    public Guid UserId { get; } = userId;
}
