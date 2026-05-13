namespace ProjectCeres.Common;

/// <summary>
/// Decides whether a DB command issued without a resolved user (Guid.Empty) is
/// firing from one of the legitimate pre-auth call sites — login, registration,
/// password-reset request, lockout-unlock confirm. Used by
/// RowLevelSecurityInterceptor to suppress the warning-log noise on those paths.
/// Adding to the allow-list is a deliberate code review, not a default.
/// </summary>
public interface IPreAuthCallSiteTagger
{
    bool IsLegitimatePreAuth();
}
