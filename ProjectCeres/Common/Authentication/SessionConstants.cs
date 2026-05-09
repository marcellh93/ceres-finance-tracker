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
}
