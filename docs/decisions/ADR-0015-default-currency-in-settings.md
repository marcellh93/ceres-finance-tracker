# ADR 0015: Default Currency Stored in Settings, Seeded to EUR

## Status: Accepted

## Context
The account creation form needs a default currency to pre-select. Three options were
considered:

1. **No default — require explicit selection:** User must pick a currency every time.
   Correct but adds friction, especially when almost all accounts share one currency.

2. **Derived from the last account created:** Implicit, fragile, and wrong for the first
   account. Rejected.

3. **Stored in Settings:** An explicit user preference, set once, applied everywhere.
   Clean and predictable.

In Phase 1 the app is single-user with a known locale (Spain, EUR). A first-run setup
prompt to configure the default currency adds UI complexity with no benefit — the correct
default is already known. In Phase 3, when new users with unknown preferences register,
a setup flow or onboarding step becomes meaningful.

## Decision
Add `DefaultCurrencyId int NOT NULL FK → Currency` to the Settings table.

- Seeds to EUR on first run alongside the other Settings defaults. No user prompt.
- Editable via the Settings page at any time.
- The account creation form pre-selects this currency. The user can override it
  per account — DefaultCurrencyId is a convenience default, not a constraint.
- First-run setup UI is deferred to Phase 3.

## Consequences

**Positive:**
- Account creation requires no currency selection in the common case (all EUR accounts)
- Consistent with how other Settings defaults work — seed and allow override
- No additional UI needed for Phase 1

**Negative:**
- A new FK column on Settings must be seeded correctly — if the EUR Currency row does
  not exist when the Settings row is created, the insert will fail. Seeding order in
  EF Core must seed Currency before Settings.
- In Phase 3, new users will need a mechanism to set their default currency during
  registration or onboarding — this is not designed yet (see Open Questions in planning.md).
