# ADR-0078 — Import Shelved from Phase 3 Beta

**Status:** Accepted (Phase 3) — product-owner decision 2026-06-29

**Date:** 2026-06-29

**Supersedes:** None. This ADR does not overturn a prior decision — it **pauses** a set of them. Placed on hold by this ADR: [ADR-0047](ADR-0047-csv-column-mapping-ux.md), [ADR-0059](ADR-0059-import-parser-abstraction.md), [ADR-0060](ADR-0060-import-ux-reconciliation-sign-based-category.md), [ADR-0061](ADR-0061-import-transfer-staging.md), [ADR-0072](ADR-0072-import-sandbox-as-separate-environment.md). (ADR-0038 is already superseded by ADR-0059 — dead, not on hold.)

## Context

The CSV/XLSX import module shipped as SPA surfaces in Batch 2 (Stage 4, 2026-05-06): the `/app/import` wizard, the `/app/import/profiles` column-mapping profiles, server-side staging, and the planned developer sandbox (Stage 11.5, ADR-0072). It carries too many unknown factors to be part of the hosted beta — parser correctness across real-world bank formats, transfer-detection accuracy, and the import atomicity contract (see `planning-phase3.md` § Pre-Stage-9 review) are all unresolved. Stabilising import to a beta-quality bar is not worth the cost for the first beta; it may become a robust feature later, but not now.

## Decision

**Shelve the import module and functionality from the Phase 3 beta until further notice.** Recoverable, not deleted — the code stays in the tree so the "maybe eventually, make it robust" path remains open.

- Removal from the user-facing beta is planned as **Stage 11.9** and executes during Stage 11 (Razor + URL cleanup), which lands before the beta. No code changes are made by this ADR.
- **Stage 11.5** (import sandbox + admin tooling) is placed **on hold** — it is tooling for a shelved feature.
- The related import ADRs (listed above) are **on hold**, not superseded.

### What "import functionality" covers

- The `/app/import` wizard and `/app/import/profiles` column-mapping profiles (SPA routes + pages).
- The **Import** item in the sidebar's TOOLS group.
- The `ImportStagedTransactions` / `ImportStagedTransfers` staging tables and the import API endpoints.
- The **entire Review page** — both its **Reconciliations** tab and its **Transfers** tab read exclusively from the `ImportStagedTransactions` / `ImportStagedTransfers` staging tables, which are written only by `ImportService`. With import shelved, both tabs are permanently empty; the Review nav item, `/review` route, and `ReviewCountProvider` are shelved alongside import.

## Open question — RESOLVED 2026-06-29

The Review/Reconciliations coupling. **Resolved at Stage 11.9 execution:** inspection of `Review.tsx` confirmed that both the Reconciliations tab and the Transfers tab read exclusively from `ImportStaged*` staging tables written only by `ImportService`. With import shelved, neither tab can ever have content. The entire Review page (nav item, `/review` route, `ReviewCountProvider`) is shelved alongside import.

## Consequences

- Import code remains in the tree but becomes unreachable once Stage 11.9 runs; nothing is deleted, so re-enabling is a matter of restoring the nav item + routes.
- Stage 11.5 does not execute in Phase 3; ADR-0072's sandbox work is deferred until import is un-shelved.
- `api-contract.md`, `models.md` (staging entities), and `testing.md` (import suites) are updated when Stage 11.9 executes to reflect the shelved-from-beta state.
- Future "make import robust" work starts from the un-shelving of this ADR, not from scratch.
- **Endpoint fence caution:** the fence in `Program.cs` uses prefix matching on `/api/import`, `/api/reconciliation-review`, and `/api/transfer-review`. A future controller whose route starts with one of those prefixes (e.g. a hypothetical `/api/important`) would be collaterally fenced. No such route exists today, but this is on record for future awareness.
