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
    private static readonly Guid UncategorizedIncomeId  = new("20000000-0000-0000-0000-000000000025");
    private static readonly Guid UncategorizedExpenseId = new("20000000-0000-0000-0000-000000000026");

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
        IFormFile file, Guid accountId, ImportColumnMappings mappings)
    {
        if (db is null || transactionService is null)
            throw new InvalidOperationException("ImportService requires db and transactionService for ImportAsync.");

        var rows   = await ParseAsync(file, mappings);
        var result = new ImportResult();

        var existingTxns = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .ToListAsync();

        foreach (var row in rows)
        {
            try
            {
                // Reconciliation pass: match by amount + date ±1 day
                var match = existingTxns.FirstOrDefault(e =>
                    e.Amount == row.Amount &&
                    Math.Abs((e.Date.ToDateTime(TimeOnly.MinValue) -
                              row.Date.ToDateTime(TimeOnly.MinValue)).TotalDays) <= 1);

                if (match is not null)
                {
                    if (!match.IsCleared)
                    {
                        await transactionService.MarkClearedAsync(match.Id, cleared: true);
                    }
                    result.RowsReconciled++;
                    continue;
                }

                // No match — create new transaction
                var categoryId = row.Amount >= 0 ? UncategorizedIncomeId : UncategorizedExpenseId;

                var vm = new TransactionCreateViewModel
                {
                    Date        = row.Date,
                    Amount      = Math.Abs(row.Amount), // store positive; direction from category type
                    Description = row.Description,
                    AccountId   = accountId,
                    CategoryId  = categoryId
                };

                var txId = await transactionService.CreateAsync(vm);
                await transactionService.MarkClearedAsync(txId, cleared: true);
                await transactionService.MarkNeedsReviewAsync(txId, needsReview: true);
                result.RowsImported++;
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
