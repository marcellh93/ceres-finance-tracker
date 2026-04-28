using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers;

public class AttachmentsController(IFileAttachmentService attachmentService) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(Guid transactionId, IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "No file selected.";
            return RedirectToAction("Edit", "Transactions", new { id = transactionId });
        }

        try
        {
            await attachmentService.UploadAsync(transactionId, file);
            TempData["SuccessMessage"] = "File uploaded.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction("Edit", "Transactions", new { id = transactionId });
    }

    // Serves files with Content-Disposition: attachment — never renders inline.
    public async Task<IActionResult> Download(Guid id)
    {
        try
        {
            var (data, contentType, fileName) = await attachmentService.GetAsync(id);
            return File(data, contentType, fileName);
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction("Index", "Transactions");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await attachmentService.DeleteAsync(id);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadForTransfer(Guid transferId, IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "No file selected.";
            return RedirectToAction("Edit", "Transfers", new { id = transferId });
        }

        try
        {
            await attachmentService.UploadForTransferAsync(transferId, file);
            TempData["SuccessMessage"] = "File uploaded.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction("Edit", "Transfers", new { id = transferId });
    }

    public async Task<IActionResult> DownloadTransfer(Guid id)
    {
        try
        {
            var (data, contentType, fileName) = await attachmentService.GetTransferAttachmentAsync(id);
            return File(data, contentType, fileName);
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction("Index", "Transfers");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTransfer(Guid id)
    {
        try
        {
            await attachmentService.DeleteTransferAttachmentAsync(id);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
