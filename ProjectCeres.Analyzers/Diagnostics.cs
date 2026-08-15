using Microsoft.CodeAnalysis;

namespace ProjectCeres.Analyzers;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor CER001_PreAuthScopeTransactionType = new(
        id: "CER001",
        title: "[PreAuthScope]-marked class must use BeginPreAuthUserScopeAsync, not BeginTransactionAsync",
        messageFormat: "Class '{0}' is marked [PreAuthScope] but calls BeginTransactionAsync directly. Use _db.BeginPreAuthUserScopeAsync(userId, ct) instead.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Pre-auth code paths must set the per-request user context before opening a transaction so RLS policies fire correctly. See ProjectCeres/Common/Authentication/PreAuthRlsScope.cs.");

    public static readonly DiagnosticDescriptor CER002_IgnoreQueryFiltersOnUserOwned = new(
        id: "CER002",
        title: "IgnoreQueryFilters() on IUserOwned via AppDbContext requires [RlsBypassJustified(ticket)]",
        messageFormat: "Call to IgnoreQueryFilters() on IUserOwned entity '{0}' via AppDbContext strips the per-user EF wall. Either route through AdminDbContext or add [RlsBypassJustified(\"CER-NNNN\")] to the containing method.",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "AppDbContext queries against IUserOwned entities must keep the EF query filter unless a documented bypass justification exists.");

    public static readonly DiagnosticDescriptor CER004_DateTimeWallClock = new(
        id: "CER004",
        title: "Use TimeProvider.GetUtcNow() instead of DateTime.UtcNow / DateTime.Now",
        messageFormat: "Direct read of '{0}' bypasses TimeProvider injection. Replace with _timeProvider.GetUtcNow().UtcDateTime or add [AllowsWallClock(\"reason\")] to the containing member.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Production code should accept TimeProvider via DI so integration tests can pin time deterministically.");

    public static readonly DiagnosticDescriptor CER010_RlsBypassJustifiedTicketFormat = new(
        id: "CER010",
        title: "[RlsBypassJustified] ticket must match ^(CER|TICKET|ADR)-\\d+$",
        messageFormat: "[RlsBypassJustified(\"{0}\")] ticket does not match the required format. Use CER-NNNN (analyzer ID), TICKET-NNNN (issue tracker), or ADR-NNNN (architecture decision record).",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Lazy justifications (\"temp\", \"TODO\") undermine the bypass-justified audit trail.");

    public static readonly DiagnosticDescriptor CER020_ResxParityMissing = new(
        id: "CER020",
        title: "EN/ES resx parity violated — culture is missing a key its sibling has",
        messageFormat: "Resource file '{0}' is missing key '{1}' that exists in '{2}'",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Translation keys must exist in every locale before merge. A missing translation surfaces as the literal key string at runtime.");

    public static readonly DiagnosticDescriptor CER005_TokenLookupDiscipline = new(
        id: "CER005",
        title: "Token entity must declare a byte[] TokenLookup property",
        messageFormat: "Token entity '{0}' must declare a 'byte[] TokenLookup' property (HMAC fingerprint with a unique index) so confirm paths look up in O(1) instead of running Argon2id over every candidate",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A class named *Token under ProjectCeres.Models implementing IUserOwned represents a stored single-use token row. It must carry a byte[] TokenLookup column (HMAC-SHA256 of the raw token, uniquely indexed) so /confirm locates its row in O(1). Stage 9.1.5.a shipped LockoutUnlockToken without it and regressed the integration suite ~15min. See docs/superpowers/specs/2026-06-09-stage-9-5f-cer005-tokenlookup-analyzer-design.md.");

    public static readonly DiagnosticDescriptor CER007_DbContextInController = new(
        id: "CER007",
        title: "Controller uses AppDbContext directly instead of a service",
        messageFormat: "'{0}' accesses AppDbContext directly — move the query or write into a service",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "ADR-0017 puts all business logic in interface-backed services; architecture.md lists 'call DbContext directly' under what controllers may NOT do. Bypassing the service layer duplicates or loses business rules, and tests that mock services cannot catch it — the 2026-08-15 audit found SessionsApiController writing two tables with no transaction and returning 500 on a duplicate IP block. Info-level while the read-path migration is in progress (30 known GET-action sites); raise to Warning once they are gone.");

    public static readonly DiagnosticDescriptor CER006_PreAuthScopeMarkerMissing = new(
        id: "CER006",
        title: "Class calling BeginPreAuthUserScopeAsync must be marked [PreAuthScope]",
        messageFormat: "Class '{0}' calls BeginPreAuthUserScopeAsync but is not marked [PreAuthScope]. Add [PreAuthScope] to the class so the pre-auth surface stays auditable.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "BeginPreAuthUserScopeAsync opens a per-request user-scoped transaction for pre-authentication writes. Any class that calls it operates on the pre-auth path and must declare [PreAuthScope] so the contract stays enforced from both directions (CER001 covers marked-class-must-use-helper; CER006 covers caller-must-be-marked). See docs/superpowers/specs/2026-06-09-stage-9-5g-cer006-preauthscope-marker-analyzer-design.md.");
}
