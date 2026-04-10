# Finance Tracker — Planning

## Index

1. [Goal](#goal)
2. [Core Financial Concepts](#core-financial-concepts)
3. [Tech Stack](#tech-stack)
4. [Financial Reports](#financial-reports)
5. [Planned Features — Phase 1 (Local MVP)](#planned-features-phase-1--local-mvp)
6. [Planned Features — Phase 2 (Local Extended)](#planned-features-phase-2--local-extended)
7. [Planned Features — Phase 3 (Hosted Beta)](#planned-features-phase-3--hosted-beta)
8. [Planned Features — Phase 4 (Freelancer / Autónomo Support)](#planned-features-phase-4--freelancer--autónomo-support)
9. [Planned Features — Phase 5 (Business Model)](#planned-features-phase-5--business-model)
10. [Project Structure](#project-structure-proposed)
11. [Testing Strategy](#testing-strategy)
12. [Open Questions / Decisions](#open-questions--decisions)
13. [Next Steps](#next-steps)

---

## Goal

A personal finance tracking web app for individuals — replacing spreadsheets and notes.
Track assets, liabilities, equity (balance sheet) and income/expenses (income statement).

**Data model approach — Option C (Personal Finance Model):**
Two distinct concepts that together cover all five account types:
- **Account** — a real financial account you hold (bank, credit card, investment, loan). Type is Asset or Liability.
- **Category** — labels the activity flowing through those accounts (Groceries, Salary, Rent). Type is Income or Expense.
- **Equity** is never stored — always derived and displayed as Net Worth (Assets − Liabilities).
- Single-entry transactions. No double-entry bookkeeping required.

This matches how individuals think about money, keeping accounting complexity out of the user's way.

---

## Core Financial Concepts

### Balance Sheet
| Category    | Examples                                      |
|-------------|-----------------------------------------------|
| Assets      | Cash, bank accounts, investments, property    |
| Liabilities | Credit card debt, loans, mortgage             |
| Equity      | Net worth = Assets − Liabilities              |

### Income Statement
| Category | Examples                                      |
|----------|-----------------------------------------------|
| Income   | Salary, freelance, dividends, rental income   |
| Expenses | Rent, groceries, utilities, subscriptions     |

---

## Tech Stack

### Backend — C# / .NET
- **Runtime:** .NET 9 (cross-platform, runs natively on macOS via the .NET SDK)
- **Framework:** ASP.NET Core MVC (server-side rendering — backend renders HTML directly, no API layer needed)
- **ORM:** Entity Framework Core
- **Database:** Microsoft SQL Server (via Docker — no native macOS build exists, but the official Docker image works on Intel Macs; Apple Silicon Macs use Azure SQL Edge instead, which is ARM-compatible and speaks the same T-SQL)
- **Package manager:** NuGet (built into `dotnet` CLI)

### Frontend — Razor Views (HTML with C# templating)
- ASP.NET Core MVC uses Razor as its templating engine
- Razor files (`.cshtml`) are essentially HTML files where you can embed C# to display dynamic data
- No JavaScript required to start — forms submit to the server, the server responds with a rendered page
- Additional frontend decisions (styling, interactivity) TBD as the project progresses

### Tooling (macOS setup needed)
- Install .NET SDK via `brew install dotnet` or the official installer
- `dotnet` CLI to scaffold, run, and build the project
- VS Code with the C# Dev Kit extension (or Rider) for IDE support

---

## Financial Reports

### Essential Reports

| Report | Description |
|--------|-------------|
| **Net Worth Statement** | Snapshot of total assets, total liabilities, and resulting equity at a given date — the personal equivalent of a balance sheet |
| **Income & Expense Summary** | Total income vs. total expenses for a selected period (month, quarter, year), with net cash flow (income − expenses) |
| **Expense Breakdown by Category** | How much was spent per category in a period — answers "where is my money going?" |
| **Account Balances Summary** | Current balance of every account, grouped by type (assets vs. liabilities) |
| **Transaction History** | Filterable list of all transactions by account, category, date range, or type |
| **Transfer History** | Filterable list of all transfers by source account, destination account, or date range — separate from transactions since transfers are neither income nor expense |

### Nice-to-Have Reports

| Report | Description |
|--------|-------------|
| **Net Worth Over Time** | Tracks equity month by month — shows whether you're building or losing wealth |
| **Monthly Cash Flow Trend** | Bar or table view of income vs. expenses across multiple months side by side |
| **Spending by Category Over Time** | How a category's spending changes month to month (e.g. groceries creeping up) |
| **Year-over-Year Comparison** | Compares income, expenses, and net worth between two calendar years |
| **Budget vs. Actual** | Once budgets are added — how much was planned vs. actually spent per category |
| **Largest Expenses** | Top N transactions by amount in a period — useful for spotting outliers |

---

## Planned Features (Phase 1 — Local MVP)

**Runs locally. Single user. No authentication. No hosting.**

The word "MVP" means Minimum Viable Product — the smallest set of features that makes the app
genuinely useful as a replacement for spreadsheets. The bar is specific: can you record every
financial event in your life, see your net worth, and understand where your money is going?
If yes, Phase 1 is done. Nothing more gets added here.

The discipline is intentional. Scope creep is the most common reason personal projects never
get finished. Phase 1 has a hard boundary: if a feature is useful but not essential to the
core loop of recording and viewing finances, it waits for Phase 2. The goal is to finish this
phase, use it daily, and let it replace your spreadsheet from day one.

A secondary purpose: Phase 1 validates the data model and architecture while the codebase is
still small. If something is fundamentally wrong with how accounts, transactions, or categories
work, the cost of fixing it here is low. That cost grows significantly once Phase 2 is built on top.

### Accounts
- Create/edit/delete accounts
- Each account has a type: Asset or Liability
- Investment accounts (brokerage, retirement funds, pensions) are treated as Asset accounts — balance is tracked at the account level, no individual holdings in this phase
- Seeded with a small set of generic placeholder accounts on first run — users are expected to rename them to their actual account names or delete them if not applicable

**Starter Accounts**

| Account | Type | Notes |
|---------|------|-------|
| Cash | Asset | Physical cash on hand — petty cash, wallet money |
| Checking Account | Asset | Main day-to-day bank account |
| Savings Account | Asset | General savings |
| Credit Card | Liability | General credit card placeholder |

These are intentionally generic. The goal is to give users something to work with immediately, not to model their actual bank setup. All can be renamed, edited, or deleted.

### Transactions
- Record transactions with: date, amount, description, category, account
- Attach one or more files to a transaction (receipts, invoices)
- Balance updates automatically per account

### Categories
- User-defined categories for income and expenses
- Seeded with a default set on first run (see below) — users can add, rename, or delete any category

**Starter Income Categories**

| Category | Notes |
|----------|-------|
| Salary | Regular employment income |
| Freelance & Self-employment | Independent work and consulting |
| Investment Returns | Dividends, interest, capital gains |
| Rental Income | Income from property |
| Business Income | Revenue from a business |
| Other Income | Anything that doesn't fit above |

**Starter Expense Categories**

| Category | Notes |
|----------|-------|
| Rent & Mortgage | Monthly housing payment |
| Utilities | Electricity, water, gas, internet, phone |
| Groceries | Supermarket and food shopping |
| Dining & Restaurants | Eating and drinking out |
| Transportation | Public transport, taxis, ride-sharing |
| Fuel | Petrol / diesel for personal vehicle |
| Healthcare & Medical | Doctor, pharmacy, dental, vision |
| Insurance | Health, home, car, life insurance premiums |
| Entertainment | Cinema, concerts, hobbies, streaming |
| Shopping & Clothing | Apparel, household goods, personal items |
| Education | Courses, books, training |
| Subscriptions | Software, memberships, recurring services |
| Personal Care | Haircut, gym, wellness |
| Travel | Flights, hotels, holidays |
| Taxes & Fees | Tax payments, bank fees, government fees |
| Home & Maintenance | Repairs, furniture, home improvement |
| Other Expenses | Anything that doesn't fit above |

### Dashboard
Always live — no generation required. Updates automatically as transactions are entered.

- Current net worth (total assets − total liabilities)
- Total income vs. total expenses for the current month
- Simple account balance list per currency

> Visual/graphical dashboard charts are planned for Phase 2. Phase 1 shows the same data as numbers only.

### Reports (Phase 1)
- Net Worth Statement
- Income & Expense Summary
- Expense Breakdown by Category
- Transaction History

---

## Planned Features (Phase 2 — Local Extended)

**Still local. Phase 1 must be fully complete and in daily use before this phase begins.**

Phase 2 adds depth — features that make the app significantly more powerful but that require
Phase 1 to be stable first. These are features that were deliberately held back, not forgotten:
they add complexity that would have slowed down Phase 1, and some of them depend on having
real data and usage patterns to build on top of.

Phase 2 also serves as the preparation gate for hosting. By the time this phase is complete,
the app should be polished and reliable enough to show to other people. That is the condition
for moving to Phase 3 — not a deadline, but a quality bar.

- Account Balances Summary report
- Nice-to-have reports (Net Worth Over Time, Monthly Cash Flow Trend, etc.)
- CSV and OFX file import (bulk-import transactions from a bank statement download)
- CSV export
- Recurring transactions
- Saved report configurations
- **Investment holdings tracking** — individual positions within an investment account (ticker/name, number of units, purchase price, current price, unrealized gain/loss). Prices updated manually, no live feed.

### Budgeting (Phase 2)

Two complementary budget types. Both must exist before Phase 3 — budgeting is a core feature
that anyone using the app should be able to evaluate before it reaches a wider audience.

**Category Budgets — monthly spending caps**
A periodic limit on how much should be spent in a given category per month.
Resets every month. Feeds into the Budget Progress chart on the dashboard.
- Set a monthly limit per expense category (e.g. Groceries ≤ €300/month)
- Dashboard shows progress bar: spent vs. limit for the current month
- Report shows actual vs. limit across months

**Goal Budgets — purpose-driven financial targets**
A named budget for a specific objective with a defined target amount and optional time window.
Transactions are tagged to a goal budget to track planned vs. actual spend.
- Create a budget with a name, target amount, currency, start date, and optional end date
- Tag individual transactions to a goal budget when recording them
- Dashboard and report show: target amount, amount spent so far, amount remaining, % used
- Examples: "Trip to Japan — €3,000", "Kitchen Renovation — €8,000", "Emergency Fund — €5,000"
- A goal budget can be marked complete or left open-ended
- One transaction can be linked to one goal budget (optional — not all transactions need one)

### Visual Dashboard (Phase 2)

Always-on charts displayed on the dashboard — no generation step required. Automatically reflect current data.
Distinct from reports: reports are formal documents you produce on demand; these are live at-a-glance visuals.

| Chart | Type | Description |
|-------|------|-------------|
| Net Worth Over Time | Line chart | Equity trend by month — are you growing or shrinking? |
| Income vs. Expenses | Bar chart | Side-by-side comparison per month for the last 6 months |
| Spending by Category | Donut chart | Breakdown of expenses for the current month |
| Account Balances | Horizontal bar chart | Balance per account, grouped by currency |
| Category Budget Progress | Progress bars | Spent vs. monthly limit per expense category |
| Goal Budget Progress | Progress bars | Spent vs. target amount per goal budget, with amount remaining |
| Monthly Cash Flow | Bar chart | Net income minus expenses per month — positive or negative |

**Technology note:** Charts require JavaScript. A lightweight charting library will be needed —
**Chart.js** is the leading candidate (small, no framework required, well-documented) but the
decision is deferred until Phase 2 begins. This is the natural point where the first JS
dependency enters the project.

---

## Planned Features (Phase 3 — Hosted Beta)

The app moves from local to a hosted server. Phase 2 must be fully complete before starting.
Goal: make the app accessible to a small group of collaborators who can help test and improve it.
This phase introduces the foundational changes needed for any multi-user product.

- **Authentication** — user registration and login with username + password (hashed with Argon2, never stored in plain text)
- **MFA (Multi-Factor Authentication)** — mandatory for all users via authenticator app (Google Authenticator, Authy, etc.). Time-based one-time codes (TOTP). No SMS — SMS-based MFA is vulnerable to SIM-swap attacks and considered weak for financial apps
- **Multi-tenancy** — all data (accounts, transactions, categories) scoped to the logged-in user
- **Per-user settings** — the single-row Settings table from Phase 1 migrates to a per-user preferences table so each user has their own number and date formatting preferences
- **Hosting setup** — deploy to a server so the app is reachable outside your machine
- No business model yet — access is invite-only for collaborators/beta testers
- **GDPR & legal compliance** — required before any user outside yourself can access the app. See [`legal.md`](legal.md) for the full checklist. Minimum before Phase 3 launch:
  - Privacy policy published and accessible
  - Legal basis for processing each type of personal data documented
  - Data retention policy enforced (auto-purge soft-deleted records; never auto-purge financial records)
  - Data breach notification procedure in place (72-hour GDPR requirement)
  - Right to erasure flow implemented — with documented exceptions for legally retained financial data

---

## Planned Features (Phase 4 — Freelancer / Autónomo Support)

Hosted and stable. Goal: extend the app to serve freelancers and autónomos specifically.
These features require Phase 3 (auth + hosting) to already be in place.

| Feature | Why it matters for autónomos |
|---------|-------------------------------|
| **Personal vs. Business flag on Accounts and Categories** | Separates personal and business activity so reports can filter by context — essential for tax purposes |
| **IVA / VAT tracking** | Autónomos in Spain charge IVA to clients and pay it quarterly to Hacienda — track IVA collected and IVA paid separately |
| **Investment income tracking** | Dividends and capital gains (realized, from selling holdings) are reportable income in Spain — tie into holdings added in Phase 2 |
| **Quarterly tax summary (Modelo 130 / 303)** | Report summarizing taxable income and IVA figures per quarter, matching what autónomos file |
| **Client tracking** | Associate income transactions with specific clients to see revenue per client |
| **Invoice reference on transactions** | Link a transaction to an invoice number for traceability |
| **Bank connectivity** | Automatic transaction import via open banking (Nordigen/GoCardless for Spanish banks under PSD2) |

---

## Planned Features (Phase 5 — Business Model)

Introduced only after Phase 4 is stable. See [`business-model.md`](business-model.md) for full detail.
Goal: make the product self-sustaining by introducing a freemium tier structure.

- Free tier: core personal finance tracking (manual entry, file import, reports)
- Premium tier: bank connectivity and autónomo features (costs offset by subscription revenue)
- Authentication and multi-tenancy from Phase 3 are prerequisites for any billing system
- **Passkeys / WebAuthn** — offer as an optional alternative to password + MFA for users who want maximum security. Private key stays on the user's device and is never transmitted; the server only holds the public key. This is the standard behind modern bank authentication and what Apple/Google Passkeys are built on. Adds significant implementation complexity — only appropriate once the auth foundation from Phase 3 is stable.

---

## Project Structure (proposed)

```
finance-tracker/
  docs/
    planning.md                        ← this file
    models.md                          ← data model definitions and normalization
    legal.md                           ← legal obligations, GDPR checklist, data retention policy
    business-model.md                  ← freemium structure, pricing, cost analysis (created in Phase 5)
  FinanceTracker/                      ← ASP.NET Core MVC project
    Controllers/                       ← one controller per feature area (Accounts, Transactions, etc.)
    Models/                            ← data models (Account, Transaction, Category) + view models
    Views/                             ← Razor templates (.cshtml), one subfolder per controller
      Accounts/
      Transactions/
      Shared/                          ← shared layout (_Layout.cshtml), partials
    wwwroot/                           ← static files (CSS, images — served directly to the browser)
    Data/                              ← EF Core DbContext and migrations
    Program.cs                         ← app entry point and configuration
  FinanceTracker.Tests/                ← xUnit test project
    Unit/                              ← unit tests (no database, no HTTP)
      Calculations/                    ← net worth, cash flow, balance, currency filtering logic
    Integration/                       ← integration tests (real database, real queries)
      Repositories/                    ← EF Core query tests
      Controllers/                     ← controller + database tests via WebApplicationFactory
```

---

## Testing Strategy

### Stack
- **xUnit** — standard testing framework for .NET (used by ASP.NET Core itself)
- **Moq** — mocking library for isolating dependencies in unit tests
- **FluentAssertions** — makes test assertions read as plain English, cleaner than default xUnit assertions

### Types of Tests and When They Apply

| Type | What it tests | Database? | Introduced |
|------|--------------|-----------|------------|
| Unit | Individual calculation or business logic method in isolation | No | Phase 1 |
| Integration | EF Core queries and controllers against a real test database | Yes | Phase 1 (as needed) |
| E2E | Full browser-driven flows | Yes | Out of scope |

E2E tests (Playwright, Selenium) are explicitly out of scope — high maintenance cost for the value they provide at this scale.

### What Gets Unit Tested (Priority Order)

1. **Financial calculations** — net worth, cash flow, account balance, currency-scoped totals. These are pure math functions and the most critical to get right.
2. **Business rules** — e.g. transfer source and destination must share the same currency, amount must be positive.
3. **Report query logic** — filters, date ranges, category/account scoping.

### What Gets Integration Tested

- EF Core queries that involve joins or aggregations (balance calculation, report queries)
- Multi-user data scoping in Phase 3 — tests that confirm one user cannot access another user's data

### Testing by Phase

| Phase | Focus |
|-------|-------|
| Phase 1 | Unit tests for all calculation and business rule logic |
| Phase 2 | Expand unit tests as features are added; integration tests for complex queries |
| Phase 3 | Integration tests for data scoping — critical before opening access to other users |
| Phase 4+ | Expand coverage alongside new features |

---

## Open Questions / Decisions

- [x] Authentication? — Confirmed: not needed for Phase 1/2 (local). Required in Phase 3 before hosting.
- [x] Should accounts be linked to a currency? — Confirmed: yes. Each account has one currency. Supported at launch: EUR, USD, GBP, COP, ARS, VED. Reports filter by currency — no conversion. See models.md for detail.
- [x] How to handle transfers between accounts? — Confirmed: a dedicated Transfer entity linking source and destination accounts directly. No category. Excluded from all income/expense reports. Cross-currency transfers not supported. Has its own Transfer History report. See models.md for detail.
- [x] Number formatting and localization — Confirmed: user-configurable preferences stored in a Settings table. Options: number format (US vs European), date format (DD/MM/YYYY, MM/DD/YYYY, YYYY-MM-DD), date separator (slash, dash, dot). Phase 1: single-row app-wide settings. Phase 3: migrates to per-user preferences when multi-user support is introduced. See models.md for detail.
- [x] Starter categories — Confirmed: app seeds a default set on first run (6 income, 17 expense categories). Users can add, rename, or delete any of them. Full list in the Phase 1 features section.
- [ ] File attachment storage for Phase 3 — local filesystem works for Phase 1/2 but doesn't scale when hosted. Decide between cloud storage (e.g. Azure Blob Storage, S3) or server disk storage before Phase 3. This decision affects how the attachment feature is architected from the start.

---

## Next Steps

1. Install .NET SDK on macOS
2. Install Docker Desktop and pull the appropriate SQL Server image (SQL Server 2022 for Intel, Azure SQL Edge for Apple Silicon)
3. Scaffold the ASP.NET Core MVC project
4. Define data models: Account, Transaction, Category
5. Set up Entity Framework Core with the SQL Server provider
6. Build Controllers and Razor Views for each feature area
7. Wire up forms and navigation between pages
