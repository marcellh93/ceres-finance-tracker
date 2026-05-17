namespace ProjectCeres.Common.Email;

/// <summary>
/// One row per Resend webhook event (delivered / bounced / complained / etc.). Stage 8e.
///
/// <para>
/// Cross-tenant by design — the webhook controller is pre-auth (the Svix signature IS the
/// authentication). When the recipient address matches a known user, <see cref="UserId"/>
/// is set; for bounces on unknown addresses (e.g. typo'd self-registration) the row is
/// still recorded with <see cref="UserId"/> = null.
/// </para>
///
/// <para>
/// Does NOT implement <see cref="IUserOwned"/> — that interface requires a non-nullable
/// <c>UserId</c> and an RLS policy. Following the <c>FailedLoginAttempt</c> precedent
/// (ADR-0067): cross-tenant operational tables stay outside the multi-tenancy wall and
/// are read through admin tooling, not through end-user-scoped query paths.
/// </para>
/// </summary>
public sealed class EmailDeliveryEvent
{
    public Guid Id { get; init; }

    /// <summary>
    /// FK to <c>AspNetUsers.Id</c> when the recipient address matched a known user; null
    /// otherwise (bounce/complaint on an unknown or typo'd address — still recorded).
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>Resend's <c>data.email_id</c> — the message identifier Resend assigns at send time.</summary>
    public string MessageId { get; init; } = "";

    /// <summary>Event type — e.g. <c>email.delivered</c>, <c>email.bounced</c>, <c>email.complained</c>.</summary>
    public string Type { get; init; } = "";

    /// <summary>
    /// The recipient address as Resend reported it (i.e. <c>data.to[0]</c>). Stored
    /// case-preserved; lookups against <c>ApplicationUser.NormalizedEmail</c> go through
    /// <c>ILookupNormalizer</c> at the call site (Stage 9.1.5.b §4.6 —
    /// <c>LowercaseLookupNormalizer</c>). Used by the index
    /// <c>(EmailAddress, OccurredAt DESC)</c>.
    /// </summary>
    public string EmailAddress { get; init; } = "";

    /// <summary>
    /// Raw webhook JSON body. Stored as Postgres <c>jsonb</c> for future ad-hoc
    /// inspection from admin tooling. Never logged.
    /// </summary>
    public string Payload { get; init; } = "";

    public DateTimeOffset OccurredAt { get; init; }
}
