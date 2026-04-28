# Import System Upgrade — CSV + XLSX Support

**Date:** 2026-04-25
**Status:** Approved
**Supersedes:** ADR-0038 (CSV-only import), ADR-0047 (CsvImportProfile schema)

---

## Context

Field evidence shows that BBVA Spain and other EU banks export statements as Excel (.xlsx)
natively and do not reliably offer CSV. ADR-0038 deferred XLSX support pending real-world
usage data. That data now exists. The deferral condition has been met.

The upgrade must maintain database normalization and apply SOLID principles throughout.

---

## Design Goals

1. Accept both CSV and XLSX bank exports with no format conversion required from the user.
2. Keep the import pipeline format-blind after parsing — duplicate detection, fingerprinting,
   and transaction writes are unchanged.
3. Apply OCP: adding a third format (OFX, PDF) means a new parser class only — no changes
   to `ImportService` or the DB schema.
4. Maintain 3NF: no duplicated structure across format-specific profile tables.

---

## Section 1 — Data Model Changes

### `ImportProfile` (renamed from `CsvImportProfile`)

The DB table is renamed. Two columns are added.

| Column | Type | Notes |
|---|---|---|
| `Id` | uuid NOT NULL | unchanged |
| `Name` | varchar NOT NULL | unchanged |
| `ColumnMappings` | jsonb NOT NULL | unchanged |
| `Format` | varchar NOT NULL DEFAULT `'Csv'` | **new** — `'Csv'` or `'Excel'` |
| `SheetName` | varchar NULL | **new** — Excel only; null = read first sheet |
| `CreatedAt` | datetime NOT NULL | unchanged |
| `DeletedAt` | datetime NULL | unchanged |

One migration handles the table rename and two new columns. Existing rows receive
`Format = 'Csv'` and `SheetName = NULL` via the column defaults — no data loss, no
manual backfill.

### `ImportFormat` enum (new C# file, no DB table)

```csharp
public enum ImportFormat
{
    Csv,
    Excel
}
```

Stored in the DB as a varchar string (`'Csv'`, `'Excel'`). Consistent with how `GoalType`
and `ReminderBehaviour` are stored. Gives compile-time exhaustiveness checking on the
parser factory switch — a missing format is a compiler warning, not a runtime crash.

### `ImportColumnMappings` (renamed from `CsvColumnMappings`)

Identical shape. Rename only — the jsonb column in the DB does not change. All references
in the service layer and ViewModels are updated.

---

## Section 2 — Service Layer (SOLID Restructure)

### Split: parsing vs. orchestration

`ImportService` currently does two things: parses files and orchestrates the import
pipeline. These responsibilities are separated.

### `IImportParser` (new interface)

```csharp
public interface IImportParser
{
    ImportFormat Format { get; }
    Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings);
}
```

### `CsvImportParser` (new class)

Extracts the existing CSV parsing logic from `ImportService` verbatim. No behavior change.
Depends on CsvHelper as before.

### `ExcelImportParser` (new class)

Uses ClosedXML (MIT-licensed). Logic:

1. Verify magic bytes (`PK\x03\x04`) before opening — rejects spoofed files.
2. Open the workbook read-only.
3. If `mappings.SheetName` is set, read that worksheet. If null, read the first worksheet.
4. Skip empty rows. Parse header row to resolve column names.
5. Apply same date parsing, decimal normalization, and sign-flip logic as `CsvImportParser`.
6. Return `IReadOnlyList<ParsedImportRow>` — identical output type.

### `ImportParserFactory` (new class)

Mirrors the existing `ReportGeneratorFactory` pattern:

```csharp
public class ImportParserFactory
{
    public IImportParser GetParser(ImportFormat format) => format switch
    {
        ImportFormat.Csv   => _csvParser,
        ImportFormat.Excel => _excelParser,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
```

Registered in DI. Each parser is a singleton — they hold no state.

### `ImportService` (slimmed down)

No longer contains parsing logic. Responsibilities:

1. Receive `IFormFile` + `ImportProfile`.
2. Call `ImportParserFactory.GetParser(profile.Format)`.
3. Call `parser.ParseAsync(file, profile.Mappings)`.
4. Run existing duplicate detection + fingerprinting + write pipeline unchanged.
5. Return `ImportResult`.

### `IImportService` (updated signature)

`CsvColumnMappings` → `ImportColumnMappings` throughout. `ImportAsync` receives an
`ImportProfile` (which carries `Format` and `SheetName`) instead of raw mappings.

---

## Section 3 — Security Model Updates

Two new rules added to `security-model.md` under **File Handling Rules**, in a new
**Import files** subsection:

### Magic bytes check for XLSX

An XLSX file is a ZIP archive. Its magic byte signature is `PK\x03\x04` (bytes 0–3).
`ExcelImportParser` verifies this before passing the stream to ClosedXML. A file with
a `.xlsx` extension that fails the check is rejected with a user-facing error:

> "The uploaded file does not appear to be a valid Excel file. Please re-export from
> your bank and try again."

This mirrors the existing magic bytes rule for transaction attachments.

### File size limit for import files

Enforce a maximum of **10 MB** at the controller level before the parser runs. ClosedXML
loads the workbook into memory — an unbounded file could exhaust server memory. The
controller returns 400 if the file exceeds the limit before any parsing occurs.

### No filesystem persistence

Import files are parsed in memory and discarded. The file stream is never written to
`uploads/` or any other filesystem path. `ParsedImportRow` objects are the only output
that persists beyond the request (as `Transaction` rows in the database).

---

## Section 4 — Testing Changes

### Renamed

| Old | New |
|---|---|
| `CsvImportProfileServiceTests` | `ImportProfileServiceTests` |
| `StandardMappings()` helper | `ValidMappings()` (unified with profile tests) |

### Modified

- `ImportApiTests.PostImport_XlsxFile_Returns400WithMessage` — **inverted**: now asserts
  a valid XLSX upload returns 200 with correct `ImportResult` shape. The old "XLSX
  rejected" assertion is deleted.
- `ImportServiceIntegrationTests` — existing CSV tests untouched. `FileFromFixture`
  helper already works for any extension.

### New fixtures

```
ProjectCeres.Tests/Fixtures/
  valid_import.xlsx       — 10 rows, same data as valid_import.csv, single worksheet
  multi_sheet.xlsx        — 2 worksheets; first sheet has valid data; second has garbage
  named_sheet.xlsx        — single worksheet named "Transactions"
```

### New tests

| Test | Type |
|---|---|
| `ExcelImportParser_ValidXlsx_Returns10Rows` | Unit |
| `ExcelImportParser_MultiSheet_ReadsFirstSheetByDefault` | Unit |
| `ExcelImportParser_NamedSheet_ReadsCorrectSheet` | Unit |
| `ExcelImportParser_SpoofedFile_ThrowsOnMagicBytesMismatch` | Unit |
| `ImportAsync_ValidXlsx_Inserts10TransactionsAllCleared` | Integration |
| `ImportParserFactory_ExcelFormat_RoutesToExcelParser` | Unit |
| `ImportParserFactory_CsvFormat_RoutesToCsvParser` | Unit |
| `ImportProfile_SheetName_PassedThroughToParser` | Unit |

---

## Section 5 — ADR and Doc Changes

| Document | Change |
|---|---|
| **ADR-0038** | Status → Superseded by ADR-0059. ADR-0059 documents the parser abstraction and format support policy. |
| **ADR-0046** | Add note: "The `IImportParser` abstraction introduced in ADR-0059 makes OFX a new parser class when the time comes — no service layer changes required." |
| **ADR-0047** | Rename all `CsvImportProfile` / `CsvColumnMappings` references to `ImportProfile` / `ImportColumnMappings`. Add `Format` and `SheetName` to the schema table. |
| **`architecture.md`** | Add `ImportParserFactory` to the "What Lives Where" service list. |
| **`testing.md`** | Update import test inventory to reflect renamed classes and new XLSX tests. |
| **`security-model.md`** | Add Import files subsection under File Handling Rules. |
| **`roadmap-phase-two.md`** | Remove XLSX rejection checklist item. Add XLSX import items to Stage 3 and Verification Checklist. |

### New Verification Checklist items (CSV Import section)

```
- [ ] Upload a valid `.xlsx` file with a correctly configured profile → transactions imported; summary shows correct counts
- [ ] Upload `.xlsx` with multiple worksheets and no SheetName set on profile → first sheet is read; import succeeds
- [ ] Upload `.xlsx` with SheetName set on profile → named sheet is read; other sheets ignored
- [ ] Upload `.xlsx` file that fails magic bytes check (spoofed extension) → rejected with user-facing error
- [ ] Upload `.xlsx` file exceeding 10 MB size limit → rejected before parsing
- [ ] Upload `.csv` file with a profile where Format = 'Csv' → still works; no regression
- [ ] `ImportFormat.Excel` profile routes to `ExcelImportParser`; `ImportFormat.Csv` routes to `CsvImportParser`
```

---

## New ADR-0059 Summary

**Title:** Import Parser Abstraction and Multi-Format Support

**Decision:** Introduce `IImportParser` / `ImportParserFactory` to decouple format-specific
parsing from the import orchestration pipeline. `ImportService` is format-blind. Each format
is a separate parser class. `ImportProfile` is a single format-agnostic entity with a
`Format` discriminator and optional `SheetName`.

**Formats supported in Phase 2:** CSV, Excel (.xlsx via ClosedXML).

**Formats deferred:** OFX (see ADR-0046), PDF (see import-process-review.md Phase 3+).

**Adding a new format:** implement `IImportParser`, register in DI, add one line to
`ImportParserFactory`. No other changes required.

---

## Open Questions

None — all design decisions resolved during brainstorming session on 2026-04-25.
