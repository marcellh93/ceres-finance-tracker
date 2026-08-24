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
[Route("api/transfers")]
[Authorize]
public class TransfersApiController(
    ITransferService transferService,
    AppDbContext db,
    IFileAttachmentService attachmentService,
    ICurrentUserAccessor user) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransferRequest request)
    {
        // [ApiController] auto-runs ModelState validation via InvalidModelStateResponseFactory (returns 422).
        // We only need explicit ValidationProblem for cross-field checks below.

        if (request.SourceAccountId == request.DestAccountId)
        {
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "One or more fields are invalid.",
                    details = new[]
                    {
                        new { field = nameof(request.DestAccountId), message = "Source and destination accounts must be different." }
                    }
                }
            });
        }

        var vm = new TransferCreateViewModel
        {
            Date            = request.Date,
            Amount          = request.Amount,
            SourceAccountId = request.SourceAccountId,
            DestAccountId   = request.DestAccountId,
            Description     = request.Description,
            IsCleared       = request.IsCleared
        };

        try
        {
            var transfer = await transferService.CreateAsync(vm);
            return Created($"/api/transfers/{transfer.Id}", new { id = transfer.Id });
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

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransferEditDto>> Get(Guid id)
    {
        var dto = await db.Transfers
            .Owned(user)
            .Where(t => t.Id == id)
            .Select(t => new TransferEditDto(
                t.Id,
                t.Date,
                t.Amount,
                t.SourceAccountId,
                t.DestAccountId,
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
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTransferRequest request)
    {
        var existing = await transferService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        if (request.SourceAccountId == request.DestAccountId)
        {
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Source and destination accounts must be different.",
                    details = Array.Empty<object>()
                }
            });
        }

        var vm = new TransferEditViewModel
        {
            Id              = id,
            Date            = request.Date,
            Amount          = request.Amount,
            SourceAccountId = request.SourceAccountId,
            DestAccountId   = request.DestAccountId,
            Description     = request.Description,
            IsCleared       = request.IsCleared
        };

        try
        {
            await transferService.UpdateAsync(vm);
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
        var existing = await transferService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        try
        {
            await transferService.DeleteAsync(id);
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
        var existing = await transferService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        try
        {
            await attachmentService.ValidateAsync(file);
            var saved = await attachmentService.UploadForTransferAsync(id, file);
            return Created($"/api/transfers/{id}/attachments/{saved.Id}", new
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
            // Composite (TransferId, UserId) FK refused the write — the parent belongs to
            // someone else. 404 per security-model.md § IDOR, matching delete/download.
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
            await attachmentService.DeleteTransferAttachmentAsync(attachmentId);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
