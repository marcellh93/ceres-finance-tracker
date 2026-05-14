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
        // request payload. Stage 7 architecture test
        // (`IgnoreQueryFilters_only_appears_in_documented_exception_paths`) allow-lists
        // this file; entry added in Stage 8a.
        var email = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(email))
            throw new EmailRecipientNotResolvableException(userId, "user not found or has no email");

        return EmailRecipient.FromVerifiedUser(email);
    }
}
