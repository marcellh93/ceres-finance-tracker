# Import Column Mapping — Debit/Credit Column Support & Sign-Flip Bug Fix

**Date:** 2026-04-29
**Phase:** 3 (Hosted Beta)
**Status:** Approved

---

## Scope

Two related fixes to the import pipeline's column mapping layer:

1. **Bug fix:** `FlipDebitSign` currently flips the sign before category inference, causing negative expenses to be classified as income. Fix the logic and correct the default value.
2. **New feature:** Support banks that export separate debit and credit columns instead of a single signed amount column. The header detector auto-detects the format; the mapping UI adapts accordingly.

Both changes are in the same module (column mapping → parsers → header detection → import UI) and ship together.

---

## Bug Fix — `FlipDebitSign` Logic

### The bug

In both `CsvImportParser` and `ExcelImportParser`:

```csharp
// Current (wrong)
if (mappings.FlipDebitSign && amount < 0)
    amount = -amount;  // -75 → +75 → ImportService classifies as UncategorizedIncome ❌
```

`ImportService` infers income vs. expense from the sign of `ParsedImportRow.Amount`:

```csharp
var categoryId = row.Amount >= 0 ? UncategorizedIncomeId : UncategorizedExpenseId;
```

Flipping the sign before this inference inverts the classification. A `-75` expense becomes `+75` → income.

### The fix

Category direction must be derived from the **raw signed amount**, before any absolute-value conversion. The correct read of `FlipDebitSign`:

- `true` — bank exports debits as negative numbers (e.g. `-100.00`). This is the standard convention for most banks. Leave the sign as-is — the existing classification logic already handles it correctly.
- `false` — bank exports debits as positive numbers (rare). Negate them so they classify as expenses.

```csharp
// Fixed logic in both parsers
var rawAmount = parsedDecimal;

// FlipDebitSign=true  → bank uses negative for debits (standard). Leave sign as-is.
// FlipDebitSign=false → bank uses positive for debits (rare). Sign is already correct
//                       as long as income rows are also positive — no action needed.
// In both cases: pass rawAmount through unchanged. The existing ImportService logic
// (negative → expense, positive → income) is correct without any flip.
var amount = rawAmount;
```

The key insight: the current parser flip (`-amount`) is the bug — it was doing the wrong thing in both cases. The correct fix is to **remove the flip entirely** for the standard case. `FlipDebitSign` as a concept only makes sense for the rare bank that exports all amounts as positive with a separate type column (not supported yet — that requires a type/direction column, which is out of scope for this spec). For the two conventions that exist in the current UI (negative debits, or positive debits with no income distinction), the sign in the file is already the correct signal.

**Practical outcome:**
- Bank exports `-75` for expense, `+200` for income → `FlipDebitSign=true` (default) → amount passes through as-is → correctly classified ✅
- Bank exports `75` for expense (no income rows, or income also positive) → `FlipDebitSign=false` → amount passes through as-is → expense misclassified as income ⚠️ (this convention requires a direction column to distinguish — out of scope)

`FlipDebitSign=false` is retained in the data model for backwards compatibility but its UI toggle is removed in a future cleanup once the debit/credit column mode covers the positive-debit case properly.

### Default value change

`FlipDebitSign` default changes from `false` to `true`. The standard bank convention is negative debits. Most users' files work correctly with `true`.

No data migration required — no `ImportProfile` records exist in production (import was only exercised through unit and integration tests).

### Missing test — add

An integration test that covers the full pipeline with `FlipDebitSign = true`:

```
FlipDebitSign=true + CSV row "-75.00,Coffee" → Transaction.CategoryId = UncategorizedExpenseId
```

This test would have caught the bug. It must be added alongside the fix.

---

## New Feature — Debit/Credit Column Support

### Problem

Some banks (notably Spanish banks like La Caixa, Santander) export two separate columns — one for debits (money out, always positive) and one for credits (money in, always positive) — instead of a single signed amount column. The current `ImportColumnMappings` has no way to represent this format.

### Data model changes

**`ImportColumnMappings`** — two new nullable fields:

```csharp
public string? DebitColumn { get; set; }   // money out — values always positive
public string? CreditColumn { get; set; }  // money in — values always positive
```

`AmountColumn` and `FlipDebitSign` are retained. Exactly one mode must be active per profile:

| Mode | `AmountColumn` | `DebitColumn` | `CreditColumn` |
|------|---------------|---------------|----------------|
| Single-column | set | null | null |
| Dual-column | null | set | set |

The service layer validates this on profile save — rejects any other combination with a descriptive error.

`ImportProfile.ColumnMappings` is stored as a JSON blob. New fields deserialize as `null` on existing profiles → single-column mode. Fully backwards compatible, no migration needed.

**`HeaderDetectionResult`** — new fields:

```csharp
public string AmountMode { get; set; }  // "single" | "debitcredit" | "ambiguous"
public string? DebitColumn { get; set; }
public string? CreditColumn { get; set; }
```

### Header detection changes

**`HeaderDetectionService`** — two new keyword lists:

```csharp
private static readonly string[] DebitKeywords  =
    ["cargo", "cargos", "debit", "débito", "débitos", "withdrawal", "salida", "salidas"];

private static readonly string[] CreditKeywords =
    ["abono", "abonos", "credit", "crédito", "créditos", "deposit", "entrada", "entradas"];
```

**Mode detection logic in `DetectAsync`:**

```
debitMatch  = BestMatch(headers, DebitKeywords)
creditMatch = BestMatch(headers, CreditKeywords)
amountMatch = BestMatch(headers, AmountKeywords)

if debitMatch != null AND creditMatch != null AND amountMatch == null  → AmountMode = "debitcredit"
if debitMatch != null AND creditMatch != null AND amountMatch != null  → AmountMode = "ambiguous"
if amountMatch != null AND (debitMatch == null OR creditMatch == null) → AmountMode = "single"
if none match                                                          → AmountMode = "single", all nulls
```

**Mismatch detection on upload:**

When the user uploads a file against a saved profile, the controller calls `DetectAsync` on the incoming file and compares its `AmountMode` against the saved profile's mode (derived from which fields are set). If they differ, the response includes `MappingMismatch: true`. The UI shows a non-blocking inline warning — the user can proceed without changing anything if they choose.

### Parser changes — both `CsvImportParser` and `ExcelImportParser`

Amount resolution before row creation:

```
if mappings.DebitColumn != null AND mappings.CreditColumn != null:
    debit  = parse(row[mappings.DebitColumn])  ?? 0   // always positive or zero
    credit = parse(row[mappings.CreditColumn]) ?? 0   // always positive or zero
    amount = credit - debit                            // negative = expense, positive = income

    if debit == 0 AND credit == 0: skip row
else:
    amount = parse(row[mappings.AmountColumn])
    apply fixed FlipDebitSign logic (see bug fix section above)
```

`FlipDebitSign` is ignored in dual-column mode — the sign is derived from column semantics.

`ParsedImportRow.Amount` is unchanged — both paths produce the same signed decimal. Everything downstream (`ImportService`, `TransferDetectionService`, reconciliation) is unaffected.

### UI changes — import profile setup form (step 2)

The form adapts based on `AmountMode` returned from the file upload in step 1.

**Single-column mode (`AmountMode = "single"`):**
- `AmountColumn` dropdown, pre-filled with detected match
- `FlipDebitSign` toggle, defaulting to `true` ("My bank exports debits as negative numbers")
- `DebitColumn` and `CreditColumn` not shown

**Dual-column mode (`AmountMode = "debitcredit"`):**
- `DebitColumn` dropdown, pre-filled with detected debit match (money out)
- `CreditColumn` dropdown, pre-filled with detected credit match (money in)
- Small hint below: *"Values in both columns should be positive. The app computes the sign automatically."*
- `AmountColumn` and `FlipDebitSign` not shown

**Ambiguous mode (`AmountMode = "ambiguous"`):**
- All three fields shown, all pre-filled with their best matches
- Inline notice: *"We detected both a single amount column and separate debit/credit columns. Select which format your file uses and leave the others blank."*
- Client-side validation: if user submits with both paths active, show inline error before the form posts

**Mismatch warning (saved profile vs. uploaded file):**
- Non-blocking inline banner above the mapping form, warning tone
- *"This file appears to use a different column format than your saved profile. Review the mapping below before importing."*
- Dismissible; user can proceed without changes

---

## What Does Not Change

- `ParsedImportRow` — no new fields
- `ImportService` amount classification logic (`>= 0 → income`, `< 0 → expense`) — unchanged
- `TransferDetectionService` — unchanged
- Reconciliation fingerprint — unchanged
- Excel magic bytes check — unchanged
- All existing single-column `ImportProfile` records — work as before (null fields = single-column mode)

---

## Testing Requirements

| Test | Type | What it covers |
|------|------|---------------|
| `FlipDebitSign=true` + `-75.00` → `UncategorizedExpense` | Integration | Bug fix regression |
| `FlipDebitSign=true` + `+200.00` → `UncategorizedIncome` | Integration | Income rows unaffected by fix |
| Dual-column: debit=`50`, credit=`0` → `amount = -50` | Unit (parser) | Debit-only row |
| Dual-column: debit=`0`, credit=`200` → `amount = +200` | Unit (parser) | Credit-only row |
| Dual-column: debit=`0`, credit=`0` → row skipped | Unit (parser) | Empty row handling |
| `AmountMode = "debitcredit"` detected from headers | Unit (detection) | Header detection |
| `AmountMode = "ambiguous"` detected from headers | Unit (detection) | Header detection |
| Mismatch flag returned when profile mode ≠ file mode | Unit (controller) | Mismatch detection |
| Profile save rejected when both paths active | Unit (service) | Validation |
| Profile save rejected when neither path active | Unit (service) | Validation |
| Dual-column import end-to-end: debit row → `UncategorizedExpense` | Integration | Full pipeline |
| Dual-column import end-to-end: credit row → `UncategorizedIncome` | Integration | Full pipeline |
