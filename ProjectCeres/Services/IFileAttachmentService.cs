using Microsoft.AspNetCore.Http;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface IFileAttachmentService
{
    Task<TransactionAttachment> UploadAsync(Guid transactionId, IFormFile file);
    Task<(byte[] Data, string ContentType, string FileName)> GetAsync(Guid attachmentId);
    Task DeleteAsync(Guid attachmentId);
}
