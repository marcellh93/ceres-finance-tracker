using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ISettingsService
{
    Task<Settings> GetAsync();
    Task UpdateAsync(SettingsEditViewModel vm);
    /// <summary>Guarantees the single Settings row exists. Call once at startup.</summary>
    Task EnsureExistsAsync();

    /// <summary>API surface. Returns NOT_FOUND only if the currency does not exist.</summary>
    Task<Result> TryUpdateAsync(UpdateSettingsRequest request);
}
