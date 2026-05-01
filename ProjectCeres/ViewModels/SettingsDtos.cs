namespace ProjectCeres.ViewModels;

/// <summary>
/// Read-only projection of <c>Settings</c> for the SPA. The full Settings entity
/// is owned by the Razor controller; only fields the client needs to render
/// values correctly are exposed here.
/// </summary>
public record SettingsDto(
    string NumberFormat,
    string DateFormat,
    string DefaultCurrencyCode,
    string DefaultCurrencySymbol);
