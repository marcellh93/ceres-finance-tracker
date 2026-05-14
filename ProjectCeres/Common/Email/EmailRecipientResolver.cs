using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;

namespace ProjectCeres.Common.Email;

public interface IEmailRecipientResolver
{
    Task<EmailRecipient> ResolveAsync(Guid userId, CancellationToken ct);
}

public sealed class EmailRecipientResolver : IEmailRecipientResolver
{
    private readonly AppDbContext _db;

    public EmailRecipientResolver(AppDbContext db) => _db = db;

    public async Task<EmailRecipient> ResolveAsync(Guid userId, CancellationToken ct)
    {
        // Cross-tenant by design: this is called from pre-auth call sites (e.g. password
        // reset triggered before the user is fully signed in) AND from authenticated
        // contexts. Reading ApplicationUser.Email is safe because the userId always came
        // from server-side context (session, token row, audit context) — never from a
        // request payload. Stage 10 architecture test allow-lists this file.
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.EmailConfirmed })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            throw new EmailRecipientNotResolvableException(userId, "user not found");
        if (string.IsNullOrWhiteSpace(user.Email))
            throw new EmailRecipientNotResolvableException(userId, "user has no email");

        return EmailRecipient.FromVerifiedUser(user.Email!);
    }
}
