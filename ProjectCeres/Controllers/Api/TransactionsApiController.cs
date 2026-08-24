using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Common.Exceptions;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/transactions")]
[Authorize]
public class TransactionsApiController(
    ITransactionService transactionService,
    AppDbContext db,
    IFileAttachmentService attachmentService,
    ICurrentUserAccessor user) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransactionRequest request)
    {
        var vm = new TransactionCreateViewModel
        {
            TransactionType = TransactionTypes.Regular,
            Date = request.Date,
            Amount = request.Amount,
            AccountId = request.AccountId,
            CategoryId = request.CategoryId,
            Description = request.Description,
            IsCleared = request.IsCleared
        };

        var id = await transactionService.CreateAsync(vm);

        return Created($"/api/transactions/{id}", new { id });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransactionEditDto>> Get(Guid id)
    {
        var dto = await db.Transactions
            .Owned(user)
            .Where(t => t.Id == id)
            .Select(t => new TransactionEditDto(
                t.Id,
                t.Date,
                t.Amount,
                t.AccountId,
                t.CategoryId,
                t.Description,
                t.IsCleared,
                t.Attachments
                    .OrderBy(a => a.UploadedAt)
                    .ThenBy(a => a.Id)
                    .Select(a => new AttachmentDto(a.Id, a.FileName, a.FileSizeBytes, a.ContentType, a.UploadedAt))
                    .ToList()))
            .SingleOrDefaultAsync();

        return dto is null ? NotFound() : dto;
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTransactionRequest request)
    {
        // [ApiController] auto-runs ModelState → 422 via InvalidModelStateResponseFactory.

        var existing = await transactionService.GetByIdForEditAsync(id);
        if (existing is null) return NotFound();

        var vm = new TransactionEditViewModel
        {
            Id              = id,
            TransactionType = TransactionTypes.Regular,
            Date            = request.Date,
            Amount          = request.Amount,
            AccountId       = request.AccountId,
            CategoryId      = request.CategoryId,
            Description     = request.Description,
            IsCleared       = request.IsCleared,
            BudgetId        = request.BudgetId,
            NeedsReview     = request.NeedsReview
        };

        try
        {
            await transactionService.UpdateAsync(vm);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = ex.Message,
                    details = Array.Empty<object>()
                }
            });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var existing = await transactionService.GetByIdForEditAsync(id);
        if (existing is null) return NotFound();

        try
        {
            await transactionService.DeleteAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = ex.Message,
                    details = Array.Empty<object>()
                }
            });
        }
    }

    [HttpPost("{id:guid}/attachments")]
    public async Task<IActionResult> UploadAttachment(Guid id, IFormFile file)
    {
        var existing = await transactionService.GetByIdForEditAsync(id);
        if (existing is null) return NotFound();

        try
        {
            await attachmentService.ValidateAsync(file);
            var saved = await attachmentService.UploadAsync(id, file);
            return Created($"/api/transactions/{id}/attachments/{saved.Id}", new
            {
                id          = saved.Id,
                fileName    = saved.FileName,
                sizeBytes   = saved.FileSizeBytes,
                contentType = saved.ContentType,
                uploadedAt  = saved.UploadedAt
            });
        }
        catch (ForeignKeyViolationException)
        {
            // The composite (ParentId, UserId) FK refused the write, which means the
            // parent belongs to someone else. security-model.md § IDOR requires a
            // resource the caller cannot see to appear not to exist — a 422 naming the
            // constraint would confirm the parent is real and leak the schema. 404 here
            // matches the delete and download paths.
            // Defence in depth: the action's own existence check already 404s before the
            // service runs, so this is unreachable today. It guards a future caller that
            // reaches the service directly.
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = ex.Message,
                    details = Array.Empty<object>()
                }
            });
        }
    }

    [HttpDelete("attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DeleteAttachment(Guid attachmentId)
    {
        try
        {
            await attachmentService.DeleteAsync(attachmentId);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
