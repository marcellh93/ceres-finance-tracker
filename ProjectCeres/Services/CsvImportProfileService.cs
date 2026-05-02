// ProjectCeres/Services/CsvImportProfileService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ImportProfileService(AppDbContext db, ICurrentUserAccessor user) : IImportProfileService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<IEnumerable<ImportProfileViewModel>> GetAllActiveAsync()
    {
        var profiles = await db.ImportProfiles
            .Owned(user)
            .Where(p => p.DeletedAt == null)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return profiles.Select(ToViewModel);
    }

    public async Task<IEnumerable<ImportProfileViewModel>> GetRecentlyDeletedAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);

        var profiles = await db.ImportProfiles
            .Owned(user)
            .Where(p => p.DeletedAt != null && p.DeletedAt > cutoff)
            .OrderByDescending(p => p.DeletedAt)
            .ToListAsync();

        return profiles.Select(ToViewModel);
    }

    public async Task<ImportProfileViewModel?> GetByIdAsync(Guid id)
    {
        var profile = await db.ImportProfiles.Owned(user).FirstOrDefaultAsync(p => p.Id == id);
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
        var profile = await db.ImportProfiles.Owned(user).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.Name           = name;
        profile.SheetName      = mappings.SheetName;
        profile.ColumnMappings = JsonSerializer.Serialize(mappings);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var profile = await db.ImportProfiles.Owned(user).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task RecoverAsync(Guid id)
    {
        var profile = await db.ImportProfiles.Owned(user).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException($"Import profile {id} not found.");

        profile.DeletedAt = null;
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // API surface (Result-returning).
    // -------------------------------------------------------------------------

    public async Task<Result<Guid>> TryCreateAsync(string name, ImportFormat format, ImportColumnMappings mappings)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result<Guid>.Fail("VALIDATION_ERROR", "Name is required.");
        if (trimmed.Length > 100)
            return Result<Guid>.Fail("VALIDATION_ERROR", "Name cannot exceed 100 characters.");

        var profile = new ImportProfile
        {
            Id             = Guid.NewGuid(),
            Name           = trimmed,
            Format         = format,
            SheetName      = mappings.SheetName,
            ColumnMappings = JsonSerializer.Serialize(mappings),
            CreatedAt      = DateTime.UtcNow
        };
        db.ImportProfiles.Add(profile);
        await db.SaveChangesAsync();
        return Result<Guid>.Ok(profile.Id);
    }

    public async Task<Result> TryUpdateAsync(Guid id, string name, ImportColumnMappings mappings)
    {
        var profile = await db.ImportProfiles.Owned(user).FirstOrDefaultAsync(p => p.Id == id);
        if (profile is null) return Result.Fail("NOT_FOUND", "Import profile not found.");

        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Fail("VALIDATION_ERROR", "Name is required.");
        if (trimmed.Length > 100)
            return Result.Fail("VALIDATION_ERROR", "Name cannot exceed 100 characters.");

        profile.Name           = trimmed;
        profile.SheetName      = mappings.SheetName;
        profile.ColumnMappings = JsonSerializer.Serialize(mappings);
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    public async Task<Result> TryDeleteAsync(Guid id)
    {
        var profile = await db.ImportProfiles.Owned(user).FirstOrDefaultAsync(p => p.Id == id);
        if (profile is null) return Result.Fail("NOT_FOUND", "Import profile not found.");
        profile.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    public async Task<Result> TryRecoverAsync(Guid id)
    {
        var profile = await db.ImportProfiles.Owned(user).FirstOrDefaultAsync(p => p.Id == id);
        if (profile is null) return Result.Fail("NOT_FOUND", "Import profile not found.");
        profile.DeletedAt = null;
        await db.SaveChangesAsync();
        return Result.Ok();
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
