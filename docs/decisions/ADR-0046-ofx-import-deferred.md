# ADR 0046: OFX Import Deferred — Revisit at Phase 3 or Phase 4

## Status: Accepted

## Context

OFX (Open Financial Exchange) is an XML-based standard for bank transaction exports with
standardised field names (`<TRNAMT>`, `<DTPOSTED>`, `<MEMO>`, `<FITID>`). Unlike CSV —
where every bank uses different column names — one OFX parser works across all banks that
support the format. OFX also includes a transaction ID (`<FITID>`) that could improve
duplicate detection beyond fingerprint matching.

Phase 2 listed OFX alongside CSV import as a potential feature. A go/no-go decision was
required before `ImportService` is designed.

Reasons for deferral:

- **CSV covers the real-world use case.** The primary target user is a Spanish freelancer.
  Spanish banks (Santander, BBVA, CaixaBank, etc.) predominantly export CSV or Excel —
  OFX is more common in US and UK banking.
- **Second format before the first is validated.** CSV import — with column mapping,
  sign-flipping, duplicate detection, and reconciliation — is already complex. Adding OFX
  doubles the import surface before Phase 2 daily use has validated the CSV flow.
- **Library maturity.** The .NET OFX parsing ecosystem (`OFXSharp`, manual XML) is less
  mature and less actively maintained than CSV parsing options.
- **No concrete demand.** OFX support is a speculative addition. If a specific bank the
  user relies on exports only OFX, that is a concrete data point — but it should come from
  real usage, not a pre-emptive design decision.

## Decision

**OFX import is not included in Phase 2.** Revisit at Phase 3 or Phase 4 scope definition.

This is not a commitment to build OFX support later. The outcome at revisit may be:
implement, defer again, or discard — depending on what Phase 2 daily use reveals about
actual import patterns and bank export formats encountered.

If OFX is eventually implemented, the `<FITID>` transaction ID should be stored and used
as the primary duplicate detection key, replacing or supplementing the fingerprint approach
from ADR-0039.

## Consequences

**Positive:**
- `ImportService` is designed and validated around one format before a second is added
- No parsing library decision needed for Phase 2
- OFX adoption is driven by real usage evidence, not speculation

**Negative:**
- Users whose banks export only OFX cannot import in Phase 2 — they must manually enter
  transactions or export CSV if their bank supports it
