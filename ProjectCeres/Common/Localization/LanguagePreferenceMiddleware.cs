using System.Globalization;

namespace ProjectCeres.Common.Localization;

/// <summary>
/// Reads the `lang` cookie set by the SPA's language toggle and applies it
/// to the request's UI culture so any Razor views (the legacy auth pages
/// still under Views/Account/* until Stage 11.8 cleanup deletes them)
/// render in the same language the SPA is showing.
///
/// Cookie attributes (set by the SPA, mirrored here for read-only consumption):
///   SameSite=Lax, Secure, NOT HttpOnly (the SPA needs to read it), 1-year expiry.
/// </summary>
public sealed class LanguagePreferenceMiddleware
{
    private static readonly string[] SupportedLangs = ["en", "es"];
    private const string CookieName = "lang";

    private readonly RequestDelegate _next;

    public LanguagePreferenceMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out var lang) &&
            !string.IsNullOrEmpty(lang) &&
            SupportedLangs.Contains(lang, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(lang);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
                // Unknown language code in cookie — ignore and fall through.
            }
        }

        await _next(context);
    }
}
