using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ImportStagedTransactionService(
    AppDbContext db,
    ITransactionService transactionService,
    ICurrentUserAccessor user) : IImportStagedTransactionService
{
    private static readonly Guid UncategorizedIncomeId  = new("20000000-0000-0000-0000-000000000025");
    private static readonly Guid UncategorizedExpenseId = new("20000000-0000-0000-0000-000000000026");

    public async Task<IReadOnlyList<ImportStagedTransaction>> GetPendingAsync() =>
        await db.ImportStagedTransactions
            .Owned(user)
            .Include(s => s.Account)
            .Include(s => s.MatchedTransaction)
            .Where(s => s.Status == StagedTransactionStatus.Pending)
            .OrderBy(s => s.ImportedAt)
            .ToListAsync();

    public async Task<int> GetPendingCountAsync() =>
        await db.ImportStagedTransactions
            .Owned(user)
            .CountAsync(s => s.Status == StagedTransactionStatus.Pending);

    public async Task ConfirmAsync(Guid id)
    {
        var staged = await db.ImportStagedTransactions.Owned(user).FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException($"Staged transaction {id} not found.");

        staged.Status     = StagedTransactionStatus.Confirmed;
        staged.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task ConfirmAllAsync()
    {
        var pending = await db.ImportStagedTransactions
            .Owned(user)
            .Where(s => s.Status == StagedTransactionStatus.Pending)
            .ToListAsync();

        foreach (var staged in pending)
        {
            staged.Status     = StagedTransactionStatus.Confirmed;
            staged.ResolvedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    public async Task<Result> TryConfirmAsync(Guid id)
    {
        if (!await db.ImportStagedTransactions.Owned(user).AnyAsync(s => s.Id == id))
            return Result.Fail("NOT_FOUND", "Staged transaction not found.");
        try
        {
            await ConfirmAsync(id);
            return Result.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail("VALIDATION_ERROR", ex.Message);
        }
    }

    public async Task<Result> TryDisputeAsync(Guid id)
    {
        if (!await db.ImportStagedTransactions.Owned(user).AnyAsync(s => s.Id == id))
            return Result.Fail("NOT_FOUND", "Staged transaction not found.");
        try
        {
            await DisputeAsync(id);
            return Result.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail("VALIDATION_ERROR", ex.Message);
        }
    }

    public async Task DisputeAsync(Guid id)
    {
        var staged = await db.ImportStagedTransactions.Owned(user).FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException($"Staged transaction {id} not found.");

        if (staged.MatchedTransactionId.HasValue)
            await transactionService.MarkClearedAsync(staged.MatchedTransactionId.Value, cleared: false);

        var categoryId = staged.RawAmount >= 0 ? UncategorizedIncomeId : UncategorizedExpenseId;
        var txId = await transactionService.CreateAsync(new TransactionCreateViewModel
        {
            Date        = staged.RawDate,
            Amount      = Math.Abs(staged.RawAmount),
            Description = staged.RawDescription,
            AccountId   = staged.AccountId,
            CategoryId  = categoryId
        });
        await transactionService.MarkNeedsReviewAsync(txId, needsReview: true);

        staged.Status     = StagedTransactionStatus.Disputed;
        staged.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
