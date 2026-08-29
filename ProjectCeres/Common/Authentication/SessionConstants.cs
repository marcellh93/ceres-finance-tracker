namespace ProjectCeres.Common.Authentication;

public static class SessionConstants
{
    public const string SessionIdClaim = "sid";

    public const string SessionCookieName    = "__Host-Session";
    public const string PersistentCookieName = "__Host-Persist";
    public const string CsrfCookieName       = "__Host-XSRF";
    public const string CsrfHeaderName       = "X-XSRF-TOKEN";

    public const string PersistentScheme = "PersistentCookie";

    public const string PendingSessionItemKey = "PendingUserSession";

    public const string LastReauthAtClaim = "last_reauth_at";

    /// <summary>HttpContext.Items key used by the login + reauth flows to pass the
    /// freshness Unix-seconds string into ApplicationUserClaimsPrincipalFactory.</summary>
    public const string LastReauthAtItemKey = "LastReauthAt";

    /// <summary>Response header set by PersistentCookieRotationMiddleware on the
    /// 401 it intentionally returns while issuing a fresh session cookie. The SPA's
    /// silent-401 seam (api-client.notifyUnauthenticatedIfApplicable) checks this
    /// header and skips the anon-transition dispatch when present — otherwise the
    /// browser would never get to retry the request with the freshly-issued cookie
    /// before being redirected to /login.</summary>
    public const string CookieRotatedHeader = "X-Ceres-Cookie-Rotated";

    /// <summary>Ephemeral (non-"remember me") sliding-cookie window. Mirrors
    /// CookieAuthenticationOptions.ExpireTimeSpan; a session with no activity past
    /// this is effectively dead. Single source of truth for cookie config + the
    /// active-sessions expiry filter.</summary>
    public static readonly TimeSpan EphemeralSlidingWindow = TimeSpan.FromMinutes(30);

    /// <summary>Persistent "remember me" cookie lifetime. Mirrors the persistent
    /// cookie's Expires (+30 days).</summary>
    public static readonly TimeSpan PersistentLifetime = TimeSpan.FromDays(30);

    /// <summary>How long a dead session row is retained before the sweep deletes it.
    /// Fixed at 90 days by security-model.md § Retention Policy (revoked UserSession
    /// rows + User-Agent strings).</summary>
    public static readonly TimeSpan RetentionHorizon = TimeSpan.FromDays(90);
}
