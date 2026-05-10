namespace ProjectCeres.Common.Email;

/// <summary>
/// Test fixture: silent no-op. Use via ConfigureTestServices when a test does
/// not care about the email side-effect. Tests that care about email should
/// use a strict Moq instead.
/// </summary>
public sealed class NoopEmailService : IEmailService
{
    public Task SendAsync(EmailMessage message, CancellationToken ct) => Task.CompletedTask;
}
