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
        messageFormat: "Resource file '{0}' is missing key '{1}' that exists in '{2}'.",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Translation keys must exist in every locale before merge. A missing translation surfaces as the literal key string at runtime.");
}
