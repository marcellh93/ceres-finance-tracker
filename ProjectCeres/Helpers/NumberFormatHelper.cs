using System.Globalization;

namespace ProjectCeres.Helpers;

public static class NumberFormatHelper
{
    /// <summary>Returns the CultureInfo matching the stored NumberFormat setting value.</summary>
    public static CultureInfo GetCulture(string? numberFormat) => numberFormat switch
    {
        "comma_decimal" => new CultureInfo("es-ES"),  // 1.234,56
        _               => CultureInfo.InvariantCulture // 1,234.56 (period_decimal or fallback)
    };

    /// <summary>Formats an amount for display — includes thousands separator.</summary>
    public static string FormatAmount(decimal value, string? numberFormat)
        => value.ToString("N2", GetCulture(numberFormat));

    /// <summary>
    /// Formats an amount for a text input — uses the correct decimal character but
    /// no thousands separator, so the value is unambiguous for re-parsing.
    /// </summary>
    public static string FormatInputValue(decimal value, string? numberFormat)
        => value.ToString("F2", GetCulture(numberFormat));
}
