using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class SettingsService(AppDbContext db, ICurrentUserAccessor user) : ISettingsService
{
    public async Task<Settings> GetAsync()
    {
        var settings = await db.Settings
            .Include(s => s.DefaultCurrency)
            .Owned(user)
            .FirstOrDefaultAsync();

        if (settings is not null) return settings;

        // First-touch insert. Concurrent callers for the same user (parallel SPA
        // requests on first login) both reach this branch; UNIQUE(UserId) makes one
        // win and the other surface a unique-violation. The loser detaches its
        // attempted entity and re-fetches the winner's row.
        var draft = CreateDefaults(user.UserId);
        db.Settings.Add(draft);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(draft).State = EntityState.Detached;
        }

        return await db.Settings
            .Include(s => s.DefaultCurrency)
            .Owned(user)
            .FirstAsync();
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    public async Task<Result> TryUpdateAsync(UpdateSettingsRequest request)
    {
        var currencyExists = await db.Currencies.AnyAsync(c => c.Id == request.DefaultCurrencyId!.Value);
        if (!currencyExists)
            return Result.Fail("INVALID_CURRENCY", "The selected currency does not exist.");

        var existing = await db.Settings.Owned(user).FirstOrDefaultAsync();
        var isNew    = existing is null;
        var settings = existing ?? CreateDefaults(user.UserId);

        settings.NumberFormat      = request.NumberFormat;
        settings.DateFormat        = request.DateFormat;
        settings.DefaultCurrencyId = request.DefaultCurrencyId!.Value;
        settings.PeriodStartDay    = ClampStartDay(request.PeriodStartDay);

        if (isNew) db.Settings.Add(settings);
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    private static int ClampStartDay(int value) => value < 1 ? 1 : value > 31 ? 31 : value;

    public async Task EnsureExistsAsync()
    {
        if (!await db.Settings.Owned(user).AnyAsync())
        {
            db.Settings.Add(CreateDefaults(user.UserId));
            await db.SaveChangesAsync();
        }
    }

    private static Settings CreateDefaults(Guid userId) => new()
    {
        UserId               = userId,
        NumberFormat         = "comma_decimal",
        DateFormat           = "DD/MM/YYYY",
        DefaultCurrencyId    = 1,
        PeriodStartDay       = 1
    };
}
