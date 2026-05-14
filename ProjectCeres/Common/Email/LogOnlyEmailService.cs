using Microsoft.Extensions.Logging;

namespace ProjectCeres.Common.Email;

/// <summary>
/// Development implementation: writes the message at Information level.
/// Dev/test workflows can grab the body (including a password-reset URL) from
/// console output. Stage 8 will replace this with a real provider.
/// </summary>
public sealed class LogOnlyEmailService : IEmailService
{
    private readonly ILogger<LogOnlyEmailService> _logger;

    public LogOnlyEmailService(ILogger<LogOnlyEmailService> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "[email/dev] To={To} Subject={Subject} BodyText={BodyText}",
            message.To.Address, message.Subject, message.BodyText);
        return Task.CompletedTask;
    }
}
