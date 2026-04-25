using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class CsvImportProfileService(AppDbContext db) : ICsvImportProfileService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<IEnumerable<CsvImportProfileViewModel>> GetAllActiveAsync()
    {
        var profiles = await db.CsvImportProfiles
            .Where(p => p.DeletedAt == null)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return profiles.Select(ToViewModel);
    }

    public async Task<IEnumerable<CsvImportProfileViewModel>> GetRecentlyDeletedAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);

        var profiles = await db.CsvImportProfiles
            .Where(p => p.DeletedAt != null && p.DeletedAt > cutoff)
            .OrderByDescending(p => p.DeletedAt)
            .ToListAsync();

        return profiles.Select(ToViewModel);
    }

    public async Task<CsvImportProfileViewModel?> GetByIdAsync(Guid id)
    {
        var profile = await db.CsvImportProfiles.FindAsync(id);
        return profile is null ? null : ToViewModel(profile);
    }

    public async Task<Guid> CreateAsync(string name, CsvColumnMappings mappings)
    {
        var profile = new CsvImportProfile
        {
            Id             = Guid.NewGuid(),
            Name           = name,
            ColumnMappings = JsonSerializer.Serialize(mappings),
            CreatedAt      = DateTime.UtcNow
        };

        db.CsvImportProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    public async Task UpdateAsync(Guid id, string name, CsvColumnMappings mappings)
    {
        var profile = await db.CsvImportProfiles.FindAsync(id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.Name           = name;
        profile.ColumnMappings = JsonSerializer.Serialize(mappings);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var profile = await db.CsvImportProfiles.FindAsync(id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task RecoverAsync(Guid id)
    {
        var profile = await db.CsvImportProfiles.FindAsync(id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.DeletedAt = null;
        await db.SaveChangesAsync();
    }

    private static CsvImportProfileViewModel ToViewModel(CsvImportProfile p) => new()
    {
        Id        = p.Id,
        Name      = p.Name,
        Mappings  = JsonSerializer.Deserialize<CsvColumnMappings>(p.ColumnMappings, JsonOpts) ?? new(),
        CreatedAt = p.CreatedAt,
        DeletedAt = p.DeletedAt
    };
}
