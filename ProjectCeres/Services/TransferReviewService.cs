using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class TransferReviewService(
    AppDbContext db,
    ITransferService transferService,
    ITransactionService transactionService,
    ICurrentUserAccessor user) : ITransferReviewService
{
    private static readonly Guid UncategorizedIncomeId  = new("20000000-0000-0000-0000-000000000025");
    private static readonly Guid UncategorizedExpenseId = new("20000000-0000-0000-0000-000000000026");

    public async Task<IReadOnlyList<ImportStagedTransfer>> GetPendingAsync() =>
        await db.ImportStagedTransfers
            .Owned(user)
            .Include(s => s.Account).ThenInclude(a => a.Currency)
            .Include(s => s.CandidateTransaction)
            .Where(s => s.Status == StagedTransferStatus.Pending)
            .OrderBy(s => s.ImportedAt)
            .ToListAsync();

    public async Task<int> GetPendingCountAsync() =>
        await db.ImportStagedTransfers
            .Owned(user)
            .CountAsync(s => s.Status == StagedTransferStatus.Pending);

    public async Task<Result> TryLinkToExistingAsync(Guid stagedId, Guid otherAccountId)
    {
        var staged = await db.ImportStagedTransfers.Owned(user).FirstOrDefaultAsync(s => s.Id == stagedId);
        if (staged is null)
            return Result.Fail("NOT_FOUND", "Staged transfer not found.");
        if (!await db.Accounts.Owned(user).AnyAsync(a => a.Id == otherAccountId))
            return Result.Fail("INVALID_ACCOUNT", "The other account does not exist.");

        var absAmount = Math.Abs(staged.RawAmount);
        var (sourceId, destId) = staged.RawAmount < 0
            ? (staged.AccountId, otherAccountId)
            : (otherAccountId, staged.AccountId);

        try
        {
            await transferService.CreateAsync(new TransferCreateViewModel
            {
                Date            = staged.RawDate,
                Amount          = absAmount,
                SourceAccountId = sourceId,
                DestAccountId   = destId,
                Description     = staged.RawDescription
            });
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail("VALIDATION_ERROR", ex.Message);
        }

        staged.Status     = StagedTransferStatus.Linked;
        staged.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    public async Task<Result> TryCreateAsTransferAsync(Guid stagedId, Guid otherAccountId)
    {
        var staged = await db.ImportStagedTransfers.Owned(user).FirstOrDefaultAsync(s => s.Id == stagedId);
        if (staged is null)
            return Result.Fail("NOT_FOUND", "Staged transfer not found.");
        if (!await db.Accounts.Owned(user).AnyAsync(a => a.Id == otherAccountId))
            return Result.Fail("INVALID_ACCOUNT", "The other account does not exist.");

        var absAmount = Math.Abs(staged.RawAmount);
        var (sourceId, destId) = staged.RawAmount < 0
            ? (staged.AccountId, otherAccountId)
            : (otherAccountId, staged.AccountId);

        try
        {
            await transferService.CreateAsync(new TransferCreateViewModel
            {
                Date            = staged.RawDate,
                Amount          = absAmount,
                SourceAccountId = sourceId,
                DestAccountId   = destId,
                Description     = staged.RawDescription
            });
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail("VALIDATION_ERROR", ex.Message);
        }

        staged.Status     = StagedTransferStatus.CreatedAsTransfer;
        staged.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    public async Task<Result> TryDismissAsTransactionAsync(Guid stagedId)
    {
        var staged = await db.ImportStagedTransfers.Owned(user).FirstOrDefaultAsync(s => s.Id == stagedId);
        if (staged is null)
            return Result.Fail("NOT_FOUND", "Staged transfer not found.");

        var categoryId = staged.RawAmount >= 0 ? UncategorizedIncomeId : UncategorizedExpenseId;

        Guid txId;
        try
        {
            txId = await transactionService.CreateAsync(new TransactionCreateViewModel
            {
                Date        = staged.RawDate,
                Amount      = Math.Abs(staged.RawAmount),
                Description = staged.RawDescription,
                AccountId   = staged.AccountId,
                CategoryId  = categoryId
            });
            await transactionService.MarkClearedAsync(txId, cleared: true);
            await transactionService.MarkNeedsReviewAsync(txId, needsReview: true);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail("VALIDATION_ERROR", ex.Message);
        }

        if (!string.IsNullOrWhiteSpace(staged.RawDescription))
        {
            var normalized = staged.RawDescription.Trim();
            var exists = await db.ImportTransferExclusions
                .Owned(user)
                .AnyAsync(e => e.DescriptionPattern == normalized);
            if (!exists)
            {
                db.ImportTransferExclusions.Add(new ImportTransferExclusion
                {
                    Id                 = Guid.NewGuid(),
                    DescriptionPattern = normalized,
                    CreatedAt          = DateTime.UtcNow
                });
            }
        }

        staged.Status     = StagedTransferStatus.DismissedAsTransaction;
        staged.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Result.Ok();
    }
}
