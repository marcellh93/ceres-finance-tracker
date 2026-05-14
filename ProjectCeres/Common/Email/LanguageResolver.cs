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

        return lang switch
        {
            "es" => Es,
            _ => En,
        };
    }
}
