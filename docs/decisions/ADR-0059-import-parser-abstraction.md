# ADR 0059: Import Parser Abstraction and Multi-Format Support

## Status: Accepted

## Context

Field evidence shows that BBVA Spain and other EU banks export statements as Excel (.xlsx)
natively and do not reliably offer CSV. ADR-0038 deferred XLSX support pending real-world
usage data. That data now exists.

`ImportService` previously handled both format detection and import orchestration. Adding
XLSX by extending a single service would violate SRP and OCP — every new format would
require modifying the service.

## Decision

Introduce `IImportParser` / `ImportParserFactory` to decouple format-specific parsing from
the import orchestration pipeline.

### Interface

```csharp
public interface IImportParser
{
    ImportFormat Format { get; }
    Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings);
}
```

### Implementations

- `CsvImportParser` — existing CSV logic, extracted from `ImportService`
- `ExcelImportParser` — new, uses ClosedXML (MIT licensed). Reads first worksheet by
  default; reads named worksheet if `ImportColumnMappings.SheetName` is set.

### Factory

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

### `ImportProfile` entity

`CsvImportProfile` renamed to `ImportProfile`. Two columns added:
- `Format varchar NOT NULL DEFAULT 'Csv'` — stored as `ImportFormat` enum string
- `SheetName varchar NULL` — Excel sheet name override; null = first sheet

### `ImportService`

Format-blind. Detects format from file extension, calls `ImportParserFactory.GetParser`,
receives `IReadOnlyList<ParsedImportRow>`, runs unchanged duplicate detection + write pipeline.

### Adding a new format

Implement `IImportParser`, add one line to `ImportParserFactory`, register in DI. No other
changes required.

## Formats supported

| Format | Phase |
|---|---|
| CSV | Phase 2 |
| Excel (.xlsx) | Phase 2 |
| OFX | Deferred (see ADR-0046) |
| PDF | Deferred (see import-process-review.md) |

## Security

XLSX files are validated via magic bytes check (`PK\x03\x04`) before being opened by
ClosedXML. Import files are parsed in memory and never written to the filesystem.
A 10 MB size limit is enforced at the controller level before any parsing.

## Consequences

**Positive:**
- Adding a new import format is a new class + one factory line — no service layer changes
- Each parser is independently testable in isolation
- `ImportService` has one responsibility: orchestration

**Negative:**
- ClosedXML is a new dependency — adds ~2 MB to the published output
- Two test layers to maintain (unit for parsers, integration for full pipeline)
