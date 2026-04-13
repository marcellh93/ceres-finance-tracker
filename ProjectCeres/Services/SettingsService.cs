using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class SettingsService(AppDbContext db) : ISettingsService
{
    public async Task<Settings> GetAsync()
    {
        var settings = await db.Settings
            .Include(s => s.DefaultCurrency)
            .FirstOrDefaultAsync();

        if (settings is null)
        {
            settings = CreateDefaults();
            db.Settings.Add(settings);
            await db.SaveChangesAsync();
        }

        return settings;
    }

    public async Task UpdateAsync(SettingsEditViewModel vm)
    {
        var settings = await db.Settings.FirstOrDefaultAsync()
            ?? CreateDefaults();

        settings.NumberFormat      = vm.NumberFormat;
        settings.DateFormat        = vm.DateFormat;
        settings.DefaultCurrencyId = vm.DefaultCurrencyId!.Value;

        if (settings.Id == 0)
            db.Settings.Add(settings);

        await db.SaveChangesAsync();
    }

    public async Task EnsureExistsAsync()
    {
        if (!await db.Settings.AnyAsync())
        {
            db.Settings.Add(CreateDefaults());
            await db.SaveChangesAsync();
        }
    }

    private static Settings CreateDefaults() => new()
    {
        Id                = 1,
        NumberFormat      = "comma_decimal",
        DateFormat        = "DD/MM/YYYY",
        DefaultCurrencyId = 1
    };
}
