using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Email;

public interface ILanguageResolver
{
    Task<CultureInfo> ResolveForUserAsync(Guid userId, CancellationToken ct);
}

public sealed class LanguageResolver : ILanguageResolver
{
    private static readonly CultureInfo En = new("en");
    private static readonly CultureInfo Es = new("es");

    private readonly AppDbContext _db;

    public LanguageResolver(AppDbContext db) => _db = db;

    public async Task<CultureInfo> ResolveForUserAsync(Guid userId, CancellationToken ct)
    {
        // Cross-tenant by design: this is consulted from pre-auth call sites (e.g.
        // password reset triggered before the user is fully signed in). userId always
        // comes from server-side context. Stage 10 architecture test allow-lists this file.
        var lang = await _db.Set<Settings>()
            .IgnoreQueryFilters()
            .Where(s => s.UserId == userId)
            .Select(s => s.Language)
            .FirstOrDefaultAsync(ct);

        // Persistent user preference wins when set — a user who has explicitly
        // chosen their email language doesn't want a one-off browser session
        // to flip every future transactional email.
        if (lang == "es") return Es;
        if (lang == "en") return En;

        // Settings.Language is empty (default for newly-registered users until
        // they visit Settings → Preferences). Stage 9.6.1 (2026-05-18): fall
        // back to the SPA's language toggle for the current request, so an
        // anonymous-ish action like password-reset emails in the same language
        // the form was just submitted in. LanguagePreferenceMiddleware (Stage 8)
        // sets CurrentUICulture from the `lang` cookie on every request, so we
        // read that as the fallback hint rather than re-parsing the cookie.
        var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return ui == "es" ? Es : En;
    }
}
