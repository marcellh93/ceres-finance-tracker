using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ImportService(AppDbContext? db = null, ITransactionService? transactionService = null) : IImportService
{
    private static readonly string[] AllowedExtensions = [".csv"];

    public async Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, CsvColumnMappings mappings)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidOperationException(
                "Only CSV files are supported. Please export your bank statement as CSV.");

        var rows = new List<ParsedImportRow>();

        using var memStream = new MemoryStream();
        await file.CopyToAsync(memStream);
        memStream.Position = 0;

        using var reader = new StreamReader(memStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
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

    public string GenerateFingerprint(DateOnly date, decimal amount, string? description, Guid accountId)
    {
        var raw   = $"{date:yyyy-MM-dd}|{amount:F2}|{description ?? ""}|{accountId}";
        var hash  = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<ImportResult> ImportAsync(
        IFormFile file, Guid accountId, Guid categoryId, CsvColumnMappings mappings)
    {
        if (db is null || transactionService is null)
            throw new InvalidOperationException("ImportService requires db and transactionService for ImportAsync.");

        var rows   = await ParseAsync(file, mappings);
        var result = new ImportResult();

        // Build set of existing fingerprints for duplicate detection.
        // A row is a duplicate candidate when date ±1 day and same amount exist in the account.
        var existingTxns = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .Select(t => new { t.Date, t.Amount, t.Description })
            .ToListAsync();

        foreach (var row in rows)
        {
            try
            {
                var fingerprint = GenerateFingerprint(row.Date, row.Amount, row.Description, accountId);

                // Duplicate candidate: same amount, description, and date within ±1 day.
                var isDuplicate = existingTxns.Any(e =>
                    e.Amount == row.Amount &&
                    e.Description == row.Description &&
                    Math.Abs((e.Date.ToDateTime(TimeOnly.MinValue) - row.Date.ToDateTime(TimeOnly.MinValue)).TotalDays) <= 1);

                var vm = new TransactionCreateViewModel
                {
                    Date        = row.Date,
                    Amount      = row.Amount,
                    Description = row.Description,
                    AccountId   = accountId,
                    CategoryId  = categoryId
                };

                var txId = await transactionService.CreateAsync(vm);

                // Mark cleared status after creation via the MarkClearedAsync method.
                if (isDuplicate)
                {
                    // Leave IsCleared = false (default) — needs manual review.
                    result.RowsFlagged++;
                }
                else
                {
                    await transactionService.MarkClearedAsync(txId, cleared: true);
                    result.RowsImported++;
                }
            }
            catch (Exception ex)
            {
                result.RowsFailed++;
                result.Errors.Add($"Row {row.Date} {row.Amount}: {ex.Message}");
            }
        }

        return result;
    }
}
