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

    Task<TransferAttachment> UploadForTransferAsync(Guid transferId, IFormFile file);
    Task<(byte[] Data, string ContentType, string FileName)> GetTransferAttachmentAsync(Guid attachmentId);
    Task DeleteTransferAttachmentAsync(Guid attachmentId);

    Task<SupportTicketAttachment> UploadForSupportTicketAsync(Guid supportTicketId, IFormFile file);
    Task<(byte[] Data, string ContentType, string FileName)> GetSupportTicketAttachmentAsync(Guid attachmentId);
    Task DeleteSupportTicketAttachmentAsync(Guid attachmentId);
}
