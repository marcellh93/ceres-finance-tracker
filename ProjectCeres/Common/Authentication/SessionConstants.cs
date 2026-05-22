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
}
