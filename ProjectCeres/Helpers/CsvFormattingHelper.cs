using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Helpers;

public static class CsvFormattingHelper
{
    public static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '@' or '+' or '-') value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    public static FileContentResult CsvFile(ControllerBase controller, List<string> lines, string fileName)
    {
        var content = string.Join("\n", lines);
        var bytes   = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(content))
            .ToArray();
        return controller.File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
