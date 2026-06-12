using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProjectCeres.Common.Email;

/// <summary>
/// E2E-only implementation: writes each message as one JSON file into a sink directory
/// so Playwright can read verify/reset/unlock links (tokens are Argon2id-hashed in the
/// DB, so the email is the only way to get the raw link). Registered ONLY under
/// ASPNETCORE_ENVIRONMENT=E2E; never in Production/Development.
/// </summary>
public sealed class FileSinkEmailService : IEmailService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly ILogger<FileSinkEmailService> _logger;

    public FileSinkEmailService(string directory, ILogger<FileSinkEmailService> logger)
    {
        _directory = directory;
        _logger = logger;
        Directory.CreateDirectory(_directory);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var payload = new
        {
            to = message.To.Address,
            subject = message.Subject,
            bodyText = message.BodyText,
            bodyHtml = message.BodyHtml,
            sentAtUtc = DateTimeOffset.UtcNow,
        };
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(_directory, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions), ct);
        _logger.LogInformation("[email/e2e-sink] wrote {Path} To={To}", path, message.To.Address);
    }
}
