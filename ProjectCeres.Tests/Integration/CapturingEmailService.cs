using ProjectCeres.Common.Email;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Test-only IEmailService that appends every send to the supplied list. Use when
/// a test needs to assert against the actual EmailMessage shape (To, Subject, body
/// fragments) rather than just "send was called once". Thread-safe via List.Add
/// inside a lock.
/// </summary>
public sealed class CapturingEmailService : IEmailService
{
    private readonly List<EmailMessage> _captured;
    private readonly object _gate = new();

    public CapturingEmailService(List<EmailMessage> captured) => _captured = captured;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        lock (_gate)
        {
            _captured.Add(message);
        }
        return Task.CompletedTask;
    }
}
