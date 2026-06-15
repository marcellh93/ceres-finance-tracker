using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;
using ResendEmailMessage = Resend.EmailMessage;

namespace ProjectCeres.Common.Email;

/// <summary>
/// Production <see cref="IEmailService"/> implementation. Wraps
/// <see cref="IResend.EmailSendAsync(ResendEmailMessage, CancellationToken)"/>
/// with a fixed-delay retry policy:
/// <list type="bullet">
///   <item>3 attempts on transient errors (5xx + 429), delays 250ms / 1s / 4s.</item>
///   <item>No retry on permanent errors (4xx other than 429) — log + rethrow.</item>
/// </list>
/// The retry loop is hand-rolled to keep the dependency surface minimal —
/// Polly / Microsoft.Extensions.Http.Resilience would add a transitive package
/// for a single retry policy.
/// </summary>
public sealed class ResendEmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly EmailOptions _opts;
    private readonly ILogger<ResendEmailService> _logger;

    private static readonly int[] RetryDelaysMs = { 250, 1000, 4000 };

    public ResendEmailService(IResend resend, IOptions<EmailOptions> opts, ILogger<ResendEmailService> logger)
    {
        _resend = resend;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        // EmailAddress and EmailAddressList both define implicit string operators,
        // so the formatted From string and the single-recipient To string are
        // converted without an explicit constructor call.
        var req = new ResendEmailMessage
        {
            From = $"{_opts.Resend.FromName} <{_opts.Resend.FromAddress}>",
            To = message.To.Address,
            Subject = message.Subject,
            HtmlBody = message.BodyHtml,
            TextBody = message.BodyText,
        };

        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _resend.EmailSendAsync(req, ct);
                return;
            }
            catch (ResendException ex) when (IsTransient(ex.StatusCode))
            {
                last = ex;
                _logger.LogWarning(
                    ex,
                    "Resend transient error (attempt {Attempt}/3) status={Status}",
                    attempt + 1,
                    ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0);
                if (attempt < 2)
                {
                    await Task.Delay(RetryDelaysMs[attempt], ct);
                }
            }
            catch (ResendException ex)
            {
                _logger.LogError(
                    ex,
                    "Resend permanent error status={Status}",
                    ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0);
                throw;
            }
        }

        throw new InvalidOperationException("Resend transient error exhausted retries.", last);
    }

    /// <summary>
    /// 429 (rate-limited) and 5xx (server-side) are treated as transient. A null
    /// status code means the SDK couldn't reach Resend at all (DNS / socket /
    /// HTTP transport failure) — also retryable.
    /// </summary>
    private static bool IsTransient(HttpStatusCode? status)
    {
        if (!status.HasValue)
        {
            return true;
        }
        return status.Value == HttpStatusCode.TooManyRequests || (int)status.Value >= 500;
    }
}
