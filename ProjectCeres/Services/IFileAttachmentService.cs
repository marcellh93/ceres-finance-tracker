using Microsoft.AspNetCore.Http;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface IFileAttachmentService
{
    /// <summary>Validates size and magic bytes without writing anything. Throws InvalidOperationException on failure.</summary>
    Task ValidateAsync(IFormFile file);
    Task<TransactionAttachment> UploadAsync(Guid transactionId, IFormFile file);
    Task<(byte[] Data, string ContentType, string FileName)> GetAsync(Guid attachmentId);
    Task DeleteAsync(Guid attachmentId);
}
