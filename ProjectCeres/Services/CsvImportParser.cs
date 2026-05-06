using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Http;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class CsvImportParser : IImportParser
{
    public ImportFormat Format => ImportFormat.Csv;

    public async Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings)
    {
        var rows = new List<ParsedImportRow>();

        using var memStream = new MemoryStream();
        await file.CopyToAsync(memStream);
        memStream.Position = 0;

        using var reader = new StreamReader(memStream);
        using var csv    = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            HeaderValidated   = null
        });

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            var dateStr   = csv.GetField(mappings.DateColumn) ?? string.Empty;
            var amountStr = csv.GetField(mappings.AmountColumn) ?? string.Empty;
            var desc      = csv.GetField(mappings.DescriptionColumn);
            var category  = mappings.CategoryColumn is not null
                ? csv.GetField(mappings.CategoryColumn)
                : null;

            if (!DateOnly.TryParseExact(dateStr, ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy"], null,
                    DateTimeStyles.None, out var date))
                continue;

            if (!TryParseAmount(amountStr, out var amount))
                continue;

            rows.Add(new ParsedImportRow
            {
                Date         = date,
                Amount       = amount,
                Description  = desc,
                CategoryName = category
            });
        }

        return rows;
    }

    // Reject thousands separators on the invariant pass — otherwise "120,54"
    // (legitimate comma decimal) silently parses as 12054. The es-ES fallback
    // handles bank CSV exports written under a comma-decimal locale.
    private static bool TryParseAmount(string raw, out decimal amount)
    {
        const NumberStyles strict = NumberStyles.AllowDecimalPoint
                                  | NumberStyles.AllowLeadingSign
                                  | NumberStyles.AllowLeadingWhite
                                  | NumberStyles.AllowTrailingWhite;
        if (decimal.TryParse(raw, strict, CultureInfo.InvariantCulture, out amount))
            return true;

        return decimal.TryParse(raw, strict, new CultureInfo("es-ES"), out amount);
    }
}
