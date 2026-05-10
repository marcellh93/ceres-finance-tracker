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
}
