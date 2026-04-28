namespace ProjectCeres.Helpers;

public static class CsvFormattingHelper
{
    public static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '@' or '+' or '-') value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    public static byte[] CsvBytes(List<string> lines)
    {
        var content = string.Join("\n", lines);
        return System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(content))
            .ToArray();
    }
}
