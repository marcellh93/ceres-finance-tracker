# ADR 0055: Saved Report Configurations — Deferred to Phase 3

## Status: Accepted

## Context

Saved report configurations were listed as a Phase 2 feature — the ability to save a
named snapshot of report parameters (date range, filters, report type) for quick re-use.

The feature was designed at a surface level: a `SavedReport` entity with a `Parameters`
JSON column, soft delete with `DeletedAt`, and a restore action. The deletion rule was
already established in CLAUDE.md.

However, a more fundamental question surfaced: what does "saving a report" actually mean
for this app? In more mature tools, saved reports are a form of report builder — users
choose which columns to show, which to hide, which filters to apply, and which calculated
fields to add. That is a significantly more complex feature than a simple parameter
snapshot.

Phase 2 is the first time these reports are being used at all. There is no usage
experience to draw from when deciding:
- Which parameters are worth saving
- Which filters users reach for repeatedly
- Whether column selection matters for this app's reports
- What a "saved report" should feel like in this specific context

Building a save mechanism before the reports themselves are understood means designing
for an imagined use case rather than a real one.

## Decision

**Saved report configurations are deferred to Phase 3.**

Reassess after Phase 2 daily use reveals:
- Which reports are used most frequently
- Which parameter combinations are repeated
- Whether a full report builder (column selection, custom filters) is warranted or whether
  a simple parameter snapshot suffices
- What the deletion and restore behaviour should feel like in practice

The `SavedReport` entity, `ISavedReportService`, and related schema are not built in
Phase 2.

## Consequences

**Positive:**
- No wasted effort designing a save mechanism for reports whose shape is not yet
  understood
- Phase 3 design is informed by real Phase 2 usage patterns

**Negative:**
- Users who run the same report repeatedly must re-enter parameters each time in Phase 2
  — a known friction point, accepted as a Phase 2 limitation
