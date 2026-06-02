using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Common;

public class IUserOwnedConformanceTests
{
    [Theory]
    [InlineData(typeof(UserSession))]
    [InlineData(typeof(UserBlockedIp))]
    [InlineData(typeof(UserMfaBackupCode))]
    [InlineData(typeof(TotpReplayEntry))]
    [InlineData(typeof(PasswordResetToken))]
    [InlineData(typeof(EmailChangeToken))]
    [InlineData(typeof(LockoutUnlockToken))]
    [InlineData(typeof(AuditLog))]
    [InlineData(typeof(EmailConfirmationToken))]
    public void Auth_internal_entity_implements_IUserOwned(Type t)
    {
        typeof(IUserOwned).IsAssignableFrom(t)
            .Should().BeTrue($"{t.Name} must implement IUserOwned so Stage 7 query filters can apply");
    }

    [Fact]
    public void FailedLoginAttempt_does_NOT_implement_IUserOwned()
    {
        // Cross-tenant by design per ADR-0067. Retention sweep iterates all rows.
        typeof(IUserOwned).IsAssignableFrom(typeof(FailedLoginAttempt))
            .Should().BeFalse();
    }
}
