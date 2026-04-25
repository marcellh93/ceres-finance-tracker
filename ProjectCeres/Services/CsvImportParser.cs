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

            if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                continue;

            if (mappings.FlipDebitSign && amount < 0)
                amount = -amount;

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
}
