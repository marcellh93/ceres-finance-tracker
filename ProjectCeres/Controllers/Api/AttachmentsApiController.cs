using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/attachments")]
[Authorize]
public class AttachmentsApiController(IFileAttachmentService attachmentService) : ControllerBase
{
    /// <summary>
    /// Streams a transaction attachment with Content-Disposition: attachment.
    /// Service-layer ownership gate: returns 404 if the parent transaction is not
    /// owned by the current user, even if the attachment id is valid.
    /// </summary>
    [HttpGet("transactions/{attachmentId:guid}")]
    public async Task<IActionResult> DownloadTransactionAttachment(Guid attachmentId)
    {
        try
        {
            var (data, contentType, fileName) = await attachmentService.GetAsync(attachmentId);
            return File(data, contentType, fileDownloadName: fileName);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpGet("transfers/{attachmentId:guid}")]
    public async Task<IActionResult> DownloadTransferAttachment(Guid attachmentId)
    {
        try
        {
            var (data, contentType, fileName) = await attachmentService.GetTransferAttachmentAsync(attachmentId);
            return File(data, contentType, fileDownloadName: fileName);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
