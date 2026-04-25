using System.Globalization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ExcelImportParser : IImportParser
{
    // XLSX files are ZIP archives — magic bytes are PK (0x50 0x4B 0x03 0x04)
    private static readonly byte[] XlsxMagicBytes = [0x50, 0x4B, 0x03, 0x04];

    public ImportFormat Format => ImportFormat.Excel;

    public async Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings)
    {
        using var memStream = new MemoryStream();
        await file.CopyToAsync(memStream);
        memStream.Position = 0;

        // Magic bytes check — reject spoofed files before opening with ClosedXML
        var header = new byte[4];
        var read   = await memStream.ReadAsync(header, 0, 4);
        if (read < 4 || !header.SequenceEqual(XlsxMagicBytes))
            throw new InvalidOperationException(
                "The uploaded file is not a valid Excel file. " +
                "Please re-export from your bank and try again.");

        memStream.Position = 0;

        using var wb = new XLWorkbook(memStream);

        var ws = mappings.SheetName is not null
            ? wb.Worksheet(mappings.SheetName)
            : wb.Worksheets.First();

        // Build column index from header row (row 1)
        var headerRow = ws.Row(1);
        var colIndex  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
            colIndex[cell.GetString()] = cell.Address.ColumnNumber;

        if (!colIndex.TryGetValue(mappings.DateColumn, out var dateCol))
            throw new InvalidOperationException($"Column '{mappings.DateColumn}' not found in worksheet.");
        if (!colIndex.TryGetValue(mappings.AmountColumn, out var amountCol))
            throw new InvalidOperationException($"Column '{mappings.AmountColumn}' not found in worksheet.");
        if (!colIndex.TryGetValue(mappings.DescriptionColumn, out var descCol))
            throw new InvalidOperationException($"Column '{mappings.DescriptionColumn}' not found in worksheet.");

        int? catCol = null;
        if (mappings.CategoryColumn is not null && colIndex.TryGetValue(mappings.CategoryColumn, out var cc))
            catCol = cc;

        var rows    = new List<ParsedImportRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);

            var dateStr   = row.Cell(dateCol).GetString();
            var amountStr = row.Cell(amountCol).GetString();
            var desc      = row.Cell(descCol).GetString();
            var category  = catCol.HasValue ? row.Cell(catCol.Value).GetString() : null;

            if (string.IsNullOrWhiteSpace(dateStr) && string.IsNullOrWhiteSpace(amountStr))
                continue;

            if (!DateOnly.TryParseExact(dateStr, ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy"], null,
                    DateTimeStyles.None, out var date))
                continue;

            if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                continue;

            if (mappings.FlipDebitSign && amount < 0)
                amount = -amount;

            rows.Add(new ParsedImportRow
            {
                Date         = date,
                Amount       = amount,
                Description  = string.IsNullOrWhiteSpace(desc) ? null : desc,
                CategoryName = string.IsNullOrWhiteSpace(category) ? null : category
            });
        }

        return rows;
    }
}
