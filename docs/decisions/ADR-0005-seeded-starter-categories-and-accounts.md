# ADR 0005: Seeded Starter Categories and Accounts on First Run

## Status: Accepted

## Context
A new user opening the app for the first time would face a blank slate — no accounts, no
categories, nothing to record a transaction against. This creates friction at the exact moment
when first impressions matter most. The user would need to manually create the basic structure
before they could enter a single transaction.

The alternative — shipping with no defaults and expecting users to set everything up — was
considered too high-friction for a personal finance tool where the goal is immediate usability.

The risk of seeding too much was also considered: overly specific or opinionated defaults
could feel wrong to users whose financial situation differs from the assumptions baked in.

## Decision
The app seeds a minimal, generic set of data on first run via EF Core data seeding:

**4 starter accounts:**
- Cash (Asset) — physical cash and petty cash
- Checking Account (Asset) — main day-to-day bank account
- Savings Account (Asset) — general savings
- Credit Card (Liability) — general credit card

**6 starter income categories:**
Salary, Freelance & Self-employment, Investment Returns, Rental Income, Business Income,
Other Income

**17 starter expense categories:**
Rent & Mortgage, Utilities, Groceries, Dining & Restaurants, Transportation, Fuel,
Healthcare & Medical, Insurance, Entertainment, Shopping & Clothing, Education,
Subscriptions, Personal Care, Travel, Taxes & Fees, Home & Maintenance, Other Expenses

All seeded records are fully editable, renameable, and deletable by the user.
They are intentionally generic — placeholders to start from, not a model of any specific
person's finances. No investment account is seeded because investment accounts are more
deliberate to set up and not universal enough to assume.

## Consequences

**Positive:**
- Users can record their first transaction immediately without any manual setup
- The default set covers the most common personal finance categories without being exhaustive
- "Other Income" and "Other Expenses" act as catch-alls, ensuring users always have
  somewhere to put a transaction even before they customise
- Freelance & Self-employment in the income defaults reflects the autónomo use case

**Negative:**
- Some users will have accounts or categories that don't match the defaults and will need
  to clean them up before the app feels personalised — minor friction
- Seed data must be maintained: if categories are renamed or restructured in a future
  version, existing installations already have the old names and won't be automatically updated
- The seeding logic must be idempotent — re-running migrations on an existing database
  must not duplicate the seed data
