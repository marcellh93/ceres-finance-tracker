# Import UX Redesign — Design Spec

**Date:** 2026-04-26  
**Phase:** 2  
**Status:** Approved for implementation planning

---

## Problem

The current import form is a wall of options with no guidance. A first-time user encounters column name fields (with no idea what their CSV headers are called), a cryptic "Flip debit sign" checkbox, a profile selector that means nothing before any profiles exist, and no sense of order. The result: friction on first use, and no path toward making repeat imports faster.

---

## Goals

1. First import as easy as possible — no jargon, no guessing.
2. Repeat imports for a known bank feel like one click.
3. Transfer detection is automatic, not manual config.
4. The form never blocks progress — ambiguous rows are handled as a separate follow-up.

---

## Approach: Smart Single Page with Progressive Disclosure

The import flow remains a single page, but complexity is hidden until the user is ready for it. Upload is the trigger that reveals everything else.

---

## Design

### 1. Import Form (Index page)

#### Initial state — file not chosen

Show only three fields:

- **Bank Statement File** — file picker, accepts `.csv` and `.xlsx`
- **Destination Account** — dropdown of active accounts
- No default category field (removed — see section 3)

Nothing else is visible. No column mapping, no profile selector, no transfer options.

#### After file upload — JS-triggered reveal

The file is read client-side to extract column headers. A **Column Mapping** section slides in below with:

- **Four dropdowns:** Date, Amount, Description, Category (optional). Each is pre-populated with the detected file headers as options. The system auto-selects the closest match by name similarity (e.g. "Fecha" → Date, "Importe" → Amount, "Concepto" → Description).
- **"My bank exports debits as negative numbers"** checkbox — plain language replacing the current "Flip debit sign" label. Help text: "Check this if withdrawals appear as negative amounts in your file (e.g. -100.00)."
- **Transfer Detection subsection** — visible but clearly optional. See section 4.

#### Saved profiles

If the user has saved profiles, a **"Use a saved profile"** dropdown appears at the top of the Column Mapping section. Selecting one fills all dropdowns automatically and collapses the manual fields. If no profiles exist, this dropdown is omitted entirely — not shown, not mentioned.

#### After first successful import — save profile offer

The Summary page gains a prompt: "Want to save these column settings for next time?" with a name field and a Save button. Skippable. If saved, the profile is immediately available on next import.

---

### 2. Column Mapping UX

Current: free-text fields where the user types a column name they may not know.

New: **dropdowns populated from the actual file headers**, with smart auto-selection. The user only needs to correct mismatches — in most cases (like the real Sabadell export reviewed during design), the auto-match will be correct.

Header auto-match rules (case-insensitive, partial match):

| Ceres field | Matches if header contains |
|---|---|
| Date | fecha, date, data, datum |
| Amount | importe, amount, monto, betrag, valor |
| Description | concepto, description, descripcion, memo, details |
| Category | categoria, category, tipo |

If no match is found, the dropdown defaults to "— Select —" and the user picks manually.

---

### 3. Categories — No Default, Auto-typed Fallback

**Removed:** the "Default Category" field on the import form.

**New behavior:** The system infers income vs. expense from the sign of the amount after applying the debit-flip setting:

- Positive amount → row is income → fallback category: **"Uncategorized Income"** (system category)
- Negative amount → row is expense → fallback category: **"Uncategorized Expense"** (system category)

These are two new system categories (IsSystem = true, not deletable) — "Uncategorized Income" under the existing Income CategoryType, "Uncategorized Expense" under the existing Expense CategoryType. No new CategoryType is needed.

Rows that land in Uncategorized Income/Expense are flagged "Needs Review" and surfaced in the transaction list. When the user opens one to categorize it, the category picker is pre-filtered to the correct type (income or expense) — they never see mismatched options.

---

### 4. Transfer Detection — Two-Pass Staging

Transfers in Ceres are movements between two accounts the user owns. Transfer detection is fully automatic — no user config required on the import form.

#### Pass 1 — during import

For each row, the importer checks:

1. **Intra-file pairing:** Does another row in the same file have the opposite sign and same absolute amount on the same date? If yes → stage both as a suspected transfer pair.
2. **Cross-account pairing:** Does an existing transaction in another Ceres account have the opposite sign, same absolute amount, and same date (±1 day tolerance)? If yes → stage as a suspected transfer with a candidate match.
3. **Keyword training:** Has the user previously dismissed a row with this description pattern as "not a transfer"? If yes → skip staging, import directly as transaction.

Rows that don't trigger any of the above import immediately as transactions (with Uncategorized fallback if needed).

#### Pass 2 — Transfer Review screen (separate, non-blocking)

Staged rows appear in a dedicated **Transfer Review** screen, accessible from the Summary page and from a persistent indicator in the nav (similar to "Needs Review" transactions).

For each staged row the user sees the raw row data and three options:

- **Link to existing transaction** — shown when Ceres found a candidate match on the other side. User confirms and the pair becomes a transfer.
- **Specify other account** — user picks which account the other side belongs to. System creates the full transfer record.
- **Not a transfer** — dismiss. Row is imported as a plain transaction instead. The description pattern is saved to the training store so the same pattern is never staged again.

#### Training store

A lightweight table (`ImportTransferExclusion`) with columns: `DescriptionPattern` (normalized keyword), `CreatedAt`. The importer checks this table during Pass 1. Populated by "Not a transfer" dismissals. No UI needed to manage it in Phase 2 — dismissal is the only write path.

---

### 5. New Database Table — `ImportStagedTransfer`

Holds rows that couldn't be auto-resolved during import.

| Column | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| ImportedAt | DateTime | When the import ran |
| AccountId | Guid | FK — the account being imported into |
| RawDate | DateTime | As parsed from the file |
| RawAmount | decimal | Signed, after debit-flip applied |
| RawDescription | string | As parsed from the file |
| CandidateTransactionId | Guid? | FK — matched transaction on other side, if found |
| Status | enum | Pending, Linked, CreatedAsTransfer, DismissedAsTransaction |
| ResolvedAt | DateTime? | When the user acted on it |

---

### 6. Summary Page Changes

After import, the summary shows:

- Rows imported (unchanged)
- Rows flagged for review (unchanged)
- **Rows staged for transfer review** (new count card)
- Rows failed (unchanged)

If staged rows > 0: link to Transfer Review screen.
If import succeeded: "Save these settings as a profile for next time?" prompt (name field + Save + Skip).

---

### 7. Out of Scope for This Iteration

- Auto-categorization by description keywords (Phase 3)
- Bulk categorization UI on the transaction list (separate improvement)
- Training store management UI (future)
- Cross-currency transfer detection

---

## Open Questions

None — all clarified during design session.
