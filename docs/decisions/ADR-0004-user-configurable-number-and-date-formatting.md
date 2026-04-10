# ADR 0004: User-Configurable Number and Date Formatting

## Status: Accepted

## Context
The app targets users in multiple countries with different formatting conventions. The primary
user is based in Spain, where the decimal separator is a comma and dates follow DD/MM/YYYY.
The US convention uses a period as the decimal separator and MM/DD/YYYY for dates.

These differences are not cosmetic — in a finance app, misreading 1.234 as one thousand
two hundred thirty-four (US) instead of one point two three four (European) is a meaningful
error. Date format ambiguity (is 04/05/2026 April 5th or May 4th?) creates similar risk.

A locale-based approach (select "es-ES" or "en-US" and derive all formatting from that) was
considered but rejected in favour of individual per-preference controls, as the user specifically
wanted to choose the date separator independently (slash, dash, or dot) regardless of locale.

## Decision
A `Settings` table stores three independent formatting preferences:
- `NumberFormat`: `"period_decimal"` (1,234.56) or `"comma_decimal"` (1.234,56)
- `DateFormat`: `"DD/MM/YYYY"`, `"MM/DD/YYYY"`, or `"YYYY-MM-DD"`
- `DateSeparator`: `"/"`, `"-"`, or `"."`

In Phase 1 (local, single user) the Settings table always contains exactly one row with
no foreign keys. In Phase 3, when the app becomes multi-user, Settings migrates to a
per-user preferences table by adding a `UserId` column and removing the single-row constraint.
The column definitions remain unchanged — only the scope changes.

## Consequences

**Positive:**
- Users in any country can configure the app to match their conventions
- Granular control over date separator is available independently of date format
- The Phase 3 migration path is straightforward — same columns, new FK
- YYYY-MM-DD is available as a third option for users who prefer the ISO standard

**Negative:**
- All date and number rendering in views must go through the settings — hardcoding formats
  anywhere in the UI is a bug waiting to happen
- File import (CSV/OFX, Phase 2) must respect the user's number format when parsing
  amounts — an imported file with period decimals must be parsed correctly even if the
  user has configured comma decimals
- The Phase 3 Settings migration requires a database migration and changes to how the
  settings are resolved (from a global singleton to a per-request user lookup)
