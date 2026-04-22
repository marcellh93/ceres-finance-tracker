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

    /// <summary>
    /// Parses a decimal string using the user's number format setting.
    /// In comma_decimal mode, a value with a period but no comma is treated as
    /// invariant format (e.g. "100.00" typed instead of "100,00") to prevent
    /// the period being silently misread as a thousands separator.
    /// </summary>
    public static bool TryParseDecimal(string raw, string? numberFormat, out decimal result)
    {
        var culture = GetCulture(numberFormat);

        // In comma_decimal mode, "100.00" looks like invariant — period is decimal separator.
        // Parsing with es-ES would treat the period as thousands sep, yielding 10000.
        var looksLikeInvariant = numberFormat == "comma_decimal"
            && raw.Contains('.')
            && !raw.Contains(',');

        var primary   = looksLikeInvariant ? CultureInfo.InvariantCulture : culture;
        var secondary = looksLikeInvariant ? culture : CultureInfo.InvariantCulture;

        if (decimal.TryParse(raw, NumberStyles.Number, primary, out result))
            return true;

        if (decimal.TryParse(raw, NumberStyles.Number, secondary, out result))
            return true;

        result = 0;
        return false;
    }
}
