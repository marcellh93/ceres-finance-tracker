; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CER001 | Reliability | Warning | [PreAuthScope]-marked class must use BeginPreAuthUserScopeAsync, not BeginTransactionAsync
CER002 | Security | Warning | IgnoreQueryFilters() on IUserOwned via AppDbContext requires [RlsBypassJustified(ticket)]
CER004 | Reliability | Warning | Use TimeProvider.GetUtcNow() instead of DateTime.UtcNow / DateTime.Now
CER010 | Style | Warning | [RlsBypassJustified] ticket must match the CER/TICKET/ADR-NNNN format
CER020 | Localization | Error | EN/ES resx parity violated — culture is missing a key its sibling has
CER005 | Reliability | Warning | *Token classes under ProjectCeres/Models/ implementing IUserOwned must declare a byte[] TokenLookup property
