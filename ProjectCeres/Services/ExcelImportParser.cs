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

        XLWorkbook wb;
        try
        {
            wb = new XLWorkbook(memStream);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The uploaded file could not be opened as an Excel workbook. " +
                "Please re-export from your bank and try again.", ex);
        }

        using (wb)
        {
            IXLWorksheet ws;
            try
            {
                ws = mappings.SheetName is not null
                    ? wb.Worksheet(mappings.SheetName)
                    : wb.Worksheets.First();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    mappings.SheetName is not null
                        ? $"Sheet '{mappings.SheetName}' was not found in the workbook."
                        : "The workbook contains no worksheets.", ex);
            }

            // Bank exports often have a logo banner, report title, or metadata above the
            // table — locate the first row with ≥2 used cells and treat that as the header.
            var headerRow = ws.RowsUsed().FirstOrDefault(r => r.CellsUsed().Count() >= 2);
            var colIndex  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (headerRow is not null)
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

            var rows         = new List<ParsedImportRow>();
            var headerRowNum = headerRow!.RowNumber();
            var lastRow      = ws.LastRowUsed()?.RowNumber() ?? headerRowNum;

            for (int r = headerRowNum + 1; r <= lastRow; r++)
            {
                var row = ws.Row(r);

                var dateCell   = row.Cell(dateCol);
                var amountCell = row.Cell(amountCol);
                var desc       = row.Cell(descCol).GetString();
                var category   = catCol.HasValue ? row.Cell(catCol.Value).GetString() : null;

                if (dateCell.IsEmpty() && amountCell.IsEmpty())
                    continue;

                if (!TryReadDate(dateCell, out var date))
                    continue;

                if (!TryReadAmount(amountCell, out var amount))
                    continue;

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

    // Excel serial-date / numeric / text cells all need to round-trip through
    // a typed accessor — GetString() formats numbers using the *thread* culture,
    // which silently corrupts values when the file's convention disagrees with
    // the host's locale (e.g. 120.54 → "120,54" → 12054).
    private static bool TryReadDate(IXLCell cell, out DateOnly date)
    {
        if (cell.DataType == XLDataType.DateTime)
        {
            date = DateOnly.FromDateTime(cell.GetDateTime());
            return true;
        }

        var text = cell.GetString();
        return DateOnly.TryParseExact(text, ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy"], null,
            DateTimeStyles.None, out date);
    }

    private static bool TryReadAmount(IXLCell cell, out decimal amount)
    {
        if (cell.DataType == XLDataType.Number)
        {
            amount = (decimal)cell.GetDouble();
            return true;
        }

        // Try invariant first WITHOUT allowing thousands separators — otherwise
        // "120,54" would silently parse as 12054 (comma read as group separator).
        // Falling back to es-ES handles the legitimate comma-decimal case.
        var text = cell.GetString();
        const NumberStyles strict = NumberStyles.AllowDecimalPoint
                                  | NumberStyles.AllowLeadingSign
                                  | NumberStyles.AllowLeadingWhite
                                  | NumberStyles.AllowTrailingWhite;
        if (decimal.TryParse(text, strict, CultureInfo.InvariantCulture, out amount))
            return true;

        return decimal.TryParse(text, strict, new CultureInfo("es-ES"), out amount);
    }
}
