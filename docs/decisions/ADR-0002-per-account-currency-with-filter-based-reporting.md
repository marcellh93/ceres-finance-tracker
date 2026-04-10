# ADR 0002: Per-Account Currency with Filter-Based Reporting

## Status: Accepted

## Context
The app targets users across multiple countries and currency zones. The primary user is based
in Spain (EUR) but also holds or tracks accounts in other currencies. The question was how
to model currency and how reports should behave when a user has accounts in more than one currency.

Three approaches were considered:
1. Single currency for the whole app (simple but excludes multi-currency users)
2. Per-account currency with automatic conversion to a base currency for unified reports
3. Per-account currency with filter-based reporting — no conversion, separate views per currency

Option 2 was rejected because exchange rates fluctuate constantly, and currencies like ARS
(Argentine Peso) and VED (Venezuelan Bolívar Digital) have volatile and sometimes dual
(official vs. parallel) rates that make automatic conversion unreliable and potentially misleading
in a financial context.

## Decision
Each account is assigned exactly one currency via a `CurrencyId` foreign key. Currency is
inherited by transactions through their account — no currency field exists on the Transaction
table. Reports filter by currency: when a user views a report, they select a currency and see
only the accounts and transactions belonging to that currency. There is no conversion.

Supported currencies at launch: EUR, USD, GBP, COP, ARS, VED.

Currency conversion is explicitly out of scope and will only be reconsidered if there is
clear user demand for it in a future phase.

## Consequences

**Positive:**
- No exchange rate data needs to be stored, fetched, or maintained
- Reports are always accurate within a currency — no misleading converted totals
- Implementation is straightforward — currency is a lookup table with a FK on Account
- Correct handling of volatile currencies (ARS, VED) without making assumptions about rates

**Negative:**
- A user with EUR and USD accounts cannot see a single unified net worth figure
- Each currency produces a separate report view — users must switch between them
- Cross-currency transfers are not supported (they would require a conversion rate),
  which is a real limitation for users who transfer money between currency zones
