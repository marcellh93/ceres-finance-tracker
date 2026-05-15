using Microsoft.AspNetCore.Http;

namespace ProjectCeres.Common;

/// <summary>
/// Helpers for setting caching-related response headers consistently across all API controllers.
/// </summary>
public static class HttpResponseCacheHeaders
{
    /// <summary>
    /// Sets <c>Cache-Control: no-store, no-cache</c> and <c>Pragma: no-cache</c> on the
    /// response. Both headers are set deliberately: <c>no-store</c> is the HTTP/1.1
    /// directive that prevents any cache from storing a copy; <c>no-cache</c> (via Pragma)
    /// is the HTTP/1.0 equivalent still honoured by some intermediary proxies.
    /// Use on any endpoint whose response contains session state or security-sensitive data.
    /// </summary>
    public static void ApplyNoStore(this HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache";
        response.Headers.Pragma = "no-cache";
    }
}
