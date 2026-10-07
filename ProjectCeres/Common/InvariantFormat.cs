using System.Globalization;

namespace ProjectCeres.Common;

/// <summary>
/// Text for API keys, HTTP headers and filenames. Machine-readable, so never dependent on the server's culture.
/// </summary>
public static class InvariantFormat
{
    public static string Month(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Day(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
