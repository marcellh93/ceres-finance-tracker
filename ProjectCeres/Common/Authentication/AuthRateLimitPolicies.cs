namespace ProjectCeres.Common.Authentication;

public static class AuthRateLimitPolicies
{
    /// <summary>10/min/IP sliding window. Applied to /login, /register, /csrf.</summary>
    public const string AuthLoginByIp = "auth-login-by-ip";

    /// <summary>10/min/user sliding window keyed off Identity.TwoFactorUserId. Applied to /login/totp.</summary>
    public const string AuthTotpByUser = "auth-totp-by-user";

    /// <summary>Fallback partition key for /login/totp requests with no MFA-pending cookie.
    /// Routes them into a single shared bucket so an attacker can't dodge the limit by stripping the cookie.</summary>
    public const string AnonymousTotpPartition = "anonymous-totp";
}
