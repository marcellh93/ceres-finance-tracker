# ADR 0047: CSV Column Mapping UX and Import Profiles

## Status: Accepted

## Context

Bank CSV exports use inconsistent column names for the same fields. "Amount" may appear
as "Importe", "Debit", "Transaction Amount", or "Valor" depending on the institution.
The CSV importer needs a strategy for handling this variation.

Two options were considered:

**Fixed format** — the importer expects a specific column layout. Users must reformat
their CSV to match. Simple to build but fragile — breaks with every bank that deviates
from the expected format and puts the burden on the user each time.

**Manual mapping UI** — after uploading a CSV, the user maps their file's column headers
to app fields. Handles any bank export format. With saved profiles, the mapping is a
one-time cost per bank rather than recurring friction.

The manual mapping approach was chosen. Saved profiles were identified as essential to
making the feature usable long-term.

A secondary question arose around profile lifecycle: if a bank changes its export format,
should the user edit the existing profile or create a new one? The answer is both — the
user chooses. A further question was whether to hard delete or soft delete profiles.
Hard delete was rejected because a column mapping represents user effort that is
disproportionate to lose permanently — the same rationale as `SavedReport`.

## Decision

### Mapping UI

On first import from a new bank, the user is shown a mapping screen:

```
Your CSV column     →    App field
─────────────────────────────────
"Fecha"             →    Date         (required)
"Importe"           →    Amount       (required)
"Concepto"          →    Description  (required)
"Categoría"         →    Category     (optional)
```

The user assigns each required app field to a CSV column. Optional fields (Category) can
be left unmapped. Once mapped, the user names the profile (e.g. "BBVA") and saves it.

On subsequent imports, if the uploaded CSV headers match a saved profile, it is
auto-applied — no re-mapping needed.

### `CsvImportProfile` schema

```
CsvImportProfile
  Id          uuid NOT NULL
  Name        varchar NOT NULL
  Mappings    jsonb NOT NULL   ← { "date": "Fecha", "amount": "Importe", ... }
  CreatedAt   datetime NOT NULL
  DeletedAt   datetime NULL
```

### Profile management — settings screen

A dedicated screen lists all active profiles with the following actions per profile:

- **Edit in place** — update the column mappings. Before saving, show a diff of what
  changed and require confirmation:
  > "You're updating the BBVA profile. This will affect future imports using this profile."
- **Create new from existing** — duplicate the profile under a new name. The original
  is preserved. Useful when a bank changes format and the user is unsure if the change
  is permanent.
- **Rename** — change the display name only, mappings unchanged.
- **Delete** — soft delete with `DeletedAt` timestamp.

### Soft delete and recovery

Deleted profiles move to a "Recently deleted" section at the bottom of the settings
screen. Each deleted profile shows:

> ~~BBVA~~ — deleted 14 days ago · **Restore** · **Delete permanently**
> *This profile will be permanently deleted in 76 days.*

- Recovery window: **90 days** from `DeletedAt`
- Days remaining shown explicitly per profile — no silent auto-purge
- User can restore at any time within the window
- User can permanently delete before the window expires
- After 90 days, the profile is hard deleted automatically — but the user has had 90 days
  of visible warning

Deleting a profile does not affect previously imported transactions. Past imports are
already in the system regardless of whether the profile still exists.

### Deletion rule summary

| Action | Rule |
|---|---|
| User deletes profile | Soft delete — `DeletedAt` set, visible in "Recently deleted" |
| User restores profile | `DeletedAt` cleared, profile returns to active list |
| User permanently deletes | Hard delete — immediate, irreversible |
| 90 days after soft delete | Hard delete — auto-purge, countdown shown throughout |

## Consequences

**Positive:**
- Any bank export format is supported — no fixed column layout required
- Mapping is a one-time cost per bank — subsequent imports are one click
- Profile edit + create-new gives the user full control when formats change
- Soft delete with visible countdown prevents accidental permanent data loss
- Deletion never affects existing imported transaction data

**Negative:**
- Mapping UI adds a step to the first import from each bank — acceptable given it only
  happens once per institution
- JSON mappings column requires careful validation — malformed mappings must be caught
  before the import pipeline attempts to use them
- 90-day auto-purge requires a background cleanup job or a check on load — minor
  infrastructure addition
