using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Http;
using ProjectCeres.ViewModels;
using System.Globalization;

namespace ProjectCeres.Services;

public class HeaderDetectionService : IHeaderDetectionService
{
    private static readonly string[] DateKeywords        = ["fecha", "date", "data", "datum"];
    private static readonly string[] AmountKeywords      = ["importe", "amount", "monto", "betrag"];
    private static readonly string[] DescriptionKeywords = ["concepto", "description", "descripcion", "memo", "details"];
    private static readonly string[] CategoryKeywords    = ["categoria", "category", "tipo"];

    public async Task<HeaderDetectionResult> DetectAsync(IFormFile file)
    {
        var headers = await ReadHeadersAsync(file);

        return new HeaderDetectionResult
        {
            Headers           = headers,
            DateColumn        = BestMatch(headers, DateKeywords),
            AmountColumn      = BestMatch(headers, AmountKeywords),
            DescriptionColumn = BestMatch(headers, DescriptionKeywords),
            CategoryColumn    = BestMatch(headers, CategoryKeywords)
        };
    }

    private static string? BestMatch(IReadOnlyList<string> headers, string[] keywords)
    {
        foreach (var header in headers)
        {
            var lower = header.ToLowerInvariant();
            if (keywords.Any(k => lower.Contains(k)))
                return header;
        }
        return null;
    }

    private static async Task<IReadOnlyList<string>> ReadHeadersAsync(IFormFile file)
    {
        using var mem = new MemoryStream();
        await file.CopyToAsync(mem);
        mem.Position = 0;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext == ".xlsx")
            return ReadXlsxHeaders(mem);

        using var reader = new StreamReader(mem);
        using var csv    = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            HeaderValidated   = null
        });
        await csv.ReadAsync();
        csv.ReadHeader();
        return csv.HeaderRecord?.ToList() ?? [];
    }

    private static IReadOnlyList<string> ReadXlsxHeaders(Stream stream)
    {
        // Scan rows until we find one where all non-empty cells look like text (not dates/numbers).
        // For simplicity in Phase 2: read the first non-empty row.
        using var wb = new ClosedXML.Excel.XLWorkbook(stream);
        var ws = wb.Worksheets.First();

        foreach (var row in ws.RowsUsed())
        {
            var cells = row.CellsUsed().Select(c => c.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (cells.Count >= 2)
                return cells;
        }
        return [];
    }
}
