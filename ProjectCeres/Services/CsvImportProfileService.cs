// ProjectCeres/Services/CsvImportProfileService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ImportProfileService(AppDbContext db) : IImportProfileService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<IEnumerable<ImportProfileViewModel>> GetAllActiveAsync()
    {
        var profiles = await db.ImportProfiles
            .Where(p => p.DeletedAt == null)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return profiles.Select(ToViewModel);
    }

    public async Task<IEnumerable<ImportProfileViewModel>> GetRecentlyDeletedAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);

        var profiles = await db.ImportProfiles
            .Where(p => p.DeletedAt != null && p.DeletedAt > cutoff)
            .OrderByDescending(p => p.DeletedAt)
            .ToListAsync();

        return profiles.Select(ToViewModel);
    }

    public async Task<ImportProfileViewModel?> GetByIdAsync(Guid id)
    {
        var profile = await db.ImportProfiles.FindAsync(id);
        return profile is null ? null : ToViewModel(profile);
    }

    public async Task<Guid> CreateAsync(string name, ImportFormat format, ImportColumnMappings mappings)
    {
        var profile = new ImportProfile
        {
            Id             = Guid.NewGuid(),
            Name           = name,
            Format         = format,
            SheetName      = mappings.SheetName,
            ColumnMappings = JsonSerializer.Serialize(mappings),
            CreatedAt      = DateTime.UtcNow
        };

        db.ImportProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    public async Task UpdateAsync(Guid id, string name, ImportColumnMappings mappings)
    {
        var profile = await db.ImportProfiles.FindAsync(id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.Name           = name;
        profile.SheetName      = mappings.SheetName;
        profile.ColumnMappings = JsonSerializer.Serialize(mappings);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var profile = await db.ImportProfiles.FindAsync(id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task RecoverAsync(Guid id)
    {
        var profile = await db.ImportProfiles.FindAsync(id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.DeletedAt = null;
        await db.SaveChangesAsync();
    }

    private static ImportProfileViewModel ToViewModel(ImportProfile p) => new()
    {
        Id        = p.Id,
        Name      = p.Name,
        Format    = p.Format,
        SheetName = p.SheetName,
        Mappings  = JsonSerializer.Deserialize<ImportColumnMappings>(p.ColumnMappings, JsonOpts) ?? new(),
        CreatedAt = p.CreatedAt,
        DeletedAt = p.DeletedAt
    };
}
