using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ProjectCeres.Helpers;
using ProjectCeres.Services;

namespace ProjectCeres.ModelBinders;

/// <summary>
/// Parses decimal form values using the user's NumberFormat setting.
/// Tolerant: falls back to invariant culture so copy-pasted values (e.g. "1234.56"
/// when set to comma_decimal) still bind correctly instead of failing silently.
/// </summary>
public class DecimalModelBinder(ISettingsService settingsService) : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None) return;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);

        var rawValue = valueResult.FirstValue;

        // Empty input: for nullable types return null (Required catches it);
        // for non-nullable leave unset so the default (0) triggers Range validation.
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            if (bindingContext.ModelMetadata.IsReferenceOrNullableType)
                bindingContext.Result = ModelBindingResult.Success(null);
            return;
        }

        var settings = await settingsService.GetAsync();
        var culture  = NumberFormatHelper.GetCulture(settings.NumberFormat);

        // Try the configured format first.
        if (decimal.TryParse(rawValue, NumberStyles.Number, culture, out var result))
        {
            bindingContext.Result = ModelBindingResult.Success(result);
            return;
        }

        // Tolerant fallback: invariant culture (handles copy-paste from external sources).
        if (decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
        {
            bindingContext.Result = ModelBindingResult.Success(result);
            return;
        }

        bindingContext.ModelState.TryAddModelError(
            bindingContext.ModelName,
            "Please enter a valid number.");
    }
}
