namespace ProjectCeres.Common.Email;

public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
