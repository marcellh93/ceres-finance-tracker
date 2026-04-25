using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ImportService(
    ImportParserFactory parserFactory,
    AppDbContext? db = null,
    ITransactionService? transactionService = null) : IImportService
{
    public async Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var format    = extension switch
        {
            ".csv"  => ImportFormat.Csv,
            ".xlsx" => ImportFormat.Excel,
            _       => throw new InvalidOperationException(
                           $"Unsupported file format '{extension}'. Please upload a CSV or Excel (.xlsx) file.")
        };

        var parser = parserFactory.GetParser(format);
        return await parser.ParseAsync(file, mappings);
    }

    public string GenerateFingerprint(DateOnly date, decimal amount, string? description, Guid accountId)
    {
        var raw  = $"{date:yyyy-MM-dd}|{amount:F2}|{description ?? ""}|{accountId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<ImportResult> ImportAsync(
        IFormFile file, Guid accountId, Guid categoryId, ImportColumnMappings mappings)
    {
        if (db is null || transactionService is null)
            throw new InvalidOperationException("ImportService requires db and transactionService for ImportAsync.");

        var rows   = await ParseAsync(file, mappings);
        var result = new ImportResult();

        var existingTxns = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .Select(t => new { t.Date, t.Amount, t.Description })
            .ToListAsync();

        foreach (var row in rows)
        {
            try
            {
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

                if (isDuplicate)
                {
                    await transactionService.MarkNeedsReviewAsync(txId, needsReview: true);
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
