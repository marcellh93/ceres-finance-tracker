# Import pipeline — known issues & design decisions

Narrative record of import-pipeline bugs, fixes, and still-open directions. The
canonical *behaviour* lives in `docs/planning-phase2.md` § Import flow (+ ADR-0046/0047/0059/0060/0061)
and the per-fix specs cited below; this doc holds the "why it broke" story and the
**how-to-apply** guidance for future work on these surfaces. Originally reviewed
2026-04-29; fixes shipped through 2026-05-06.

## Still open

### 1. Opening-balance date gate — direction agreed, NOT specced

**Problem.** The service layer blocks any transaction/transfer/liability-payment dated
before the opening-balance date. Sensible for solo manual entry; breaks CSV import — a
user who sets opening balance to today and imports 6 months of history gets everything
blocked. The date gate conflates two concerns: opening balance's real job is to reconcile
the app's derived balance with the bank's actual balance on a chosen date; the date gate
is separate.

**Agreed direction.** Drop the "earliest date" enforcement entirely (opening balance is a
*balance anchor*, not a date gate). When import detects rows before the anchor, offer a
one-click "move the anchor back" instead of hard-blocking. Onboarding wizard branches at
Step 2 ("Do you have a bank CSV?") → if yes, import first and derive the anchor from the
oldest row.

**How to apply.** Flag it if anyone proposes enforcing the opening-balance date as a hard
constraint — the direction is to relax it. Needs its own brainstorm + spec.

### 4. Transfer-detection false positives — brainstorm in progress

**Pending spec:** `docs/superpowers/specs/2026-04-29-import-transfer-detection-confidence-design-PENDING.md`

**Problem.** Detection uses only amount + date ±1 day, so two unrelated transactions on
different accounts with the same amount+date get falsely staged as a transfer candidate,
blocking the row from importing.

**Agreed direction.** High confidence (description matches transfer keywords + amount +
date) → auto-stage. Low confidence (amount + date only) → import normally with a "possible
transfer?" badge. Intra-file opposite-amount same-date pairs → still auto-stage
(unambiguous). **Open questions:** where the "possible transfer" flag lives (Transaction
column vs separate table); how the user confirms from the transaction list; whether the
keyword list is user-editable; whether suggestions expire.

**How to apply.** Do not implement transfer-detection changes until the brainstorm
completes and a spec is written.

## Shipped fixes (how-to-apply guidance retained)

### 2. FlipDebitSign bug — fixed 2026-05-06
**Spec:** `docs/superpowers/specs/2026-04-29-import-column-mapping-debit-credit-design.md`.
`FlipDebitSign=true` flipped `-75`→`+75` in the parser *before* category inference, so
expenses imported as income (hit a real BBVA file). Fix removed the flip in both parsers;
classification is now driven entirely by the raw signed amount (`ImportService.cs`). The
flag is a back-compat no-op (spec line 65). **How to apply:** when the debit/credit-column
feature lands, remove the `FlipDebitSign` toggle UI; until then it stays visible but inert.

### 3. Debit/Credit column support — specced, not yet implemented
**Spec:** same as #2. Banks like La Caixa/Santander export separate Cargo/Abono columns
(both positive) instead of one signed Amount. Plan: `ImportColumnMappings` gains nullable
`DebitColumn`/`CreditColumn`; `HeaderDetectionResult` gains `AmountMode`
("single"|"debitcredit"|"ambiguous"); parsers compute `amount = credit − debit`; profile
UI adapts to detected mode with a mismatch warning.

### 5. Header-row detection mismatch — fixed 2026-05-06
`HeaderDetectionService.ReadXlsxHeaders` scans for the first row with ≥2 used cells (banks
put banners above the table), but `ExcelImportParser.ParseAsync` had hardcoded `ws.Row(1)`
— BBVA headers on row 5 threw `Column 'Fecha' not found` while the dropdown showed it
selected. **How to apply:** keep the two in sync — both use "first `RowsUsed()` row whose
`CellsUsed().Count() ≥ 2`". If a future layout fools the heuristic, promote header-row
detection to a shared helper and/or let the user pick the header row. (CSV parser
unchanged — bank CSVs rarely have banner rows.)

### 6. Numeric-cell amount corruption under comma-decimal hosts — fixed 2026-05-06
Under a comma-decimal culture (`es-ES`), `IXLCell.GetString()` returned `"120,54"`; the
parser's `decimal.TryParse(..., NumberStyles.Any, InvariantCulture)` read the comma as a
thousands separator → `12054`, a silent 100× inflation (invisible on `en-US` dev machines).
Fix branches on `IXLCell.DataType`: `Number`→`GetDouble()`, `DateTime`→`GetDateTime()` (no
string round-trip); text → strict InvariantCulture (no group separator) then `es-ES`
fallback. CSV parser fixed symmetrically. **How to apply:** for a new bank-export parser,
prefer typed cell accessors (`GetDouble`/`GetDateTime`) over `GetString` for
numeric/temporal columns; never use `NumberStyles.Any` with invariant culture without
rejecting group separators or trying multiple cultures.

### 7. Transfer detection blind to LiabilityPayment / Transfer records — fixed 2026-05-06
`TransferDetectionService.Detect` scanned only `db.Transactions`, so existing
LiabilityPayments and Transfers (two-sided moves stored as single records) were invisible —
a manually-entered Debt Payment then importing the matching card statement created a silent
duplicate income row. Fix: `Detect` also takes `IReadOnlyList<LiabilityPayment>` +
`IReadOnlyList<Transfer>`, matches each by its own account-side fields + amount + ±1-day,
and stages an `ImportStagedTransfer` with `CandidateTransactionId = null` (warn-only, no
linkable Transaction). Chosen over auto-link because the user asked to be warned. **How to
apply:** a "Link to existing payment/transfer" affordance is the deferred Option B —
`ImportStagedTransfer` would need `CandidateLiabilityPaymentId`/`CandidateTransferId` +
discriminator, and `TransferReviewService.LinkToExistingAsync` extended. Track as Phase 3
polish.
