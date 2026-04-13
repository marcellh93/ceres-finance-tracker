# Project Ceres — Planning

## Index

1. [Goal](#goal)
2. [Planned Features — Phase 1 (Local MVP)](#planned-features-phase-1--local-mvp)
3. [Testing Strategy](#testing-strategy)
4. [Open Questions / Decisions](#open-questions--decisions)
5. [Next Steps](#next-steps)

> **Other phases:** [Phase 2](planning-phase2.md) · [Phase 3](planning-phase3.md) · [Phase 4 & 5](planning-future.md)
> **Tech stack:** See `CLAUDE.md` (authoritative) or [architecture.md](architecture.md) for layer detail.
> **Data model:** See [models.md](models.md). **Financial reports:** See [planning-phase2.md](planning-phase2.md#financial-reports).

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

### Working Assumptions

These are decisions that are in effect but have not been captured as formal open questions. They apply for Phase 1 and 2 and should be revisited before Phase 3.

- **Single timezone:** Transaction dates are local dates. Reports use local date boundaries. No timezone conversion.
- **Amounts always positive:** Direction (income vs. expense) is derived from category type, not from a negative sign on the amount. Bank import logic must convert negative debits to positive amounts.
- **Reports are synchronous:** All report queries run within a single HTTP request. No background jobs or caching. Acceptable for Phase 1/2 user volumes.
- **Phase 1 is personal use first:** The primary purpose of Phase 1 is to build something the developer uses daily. Real usage will surface data model or UX issues before Phase 3 opens the app to others. This is intentional — not just a technical constraint.
- **Accessible HTML is a baseline, not a feature:** Razor Views must use semantic HTML from the start — proper `<label>` elements, heading hierarchy, keyboard-navigable forms, sufficient color contrast. This is a quality bar applied to all phases, not a separate deliverable. Full WCAG 2.1 AA compliance testing is a Phase 3 requirement before opening to other users (see Open Questions).
- **SOLID — apply S and D, defer the rest:** Controllers are thin — HTTP handling only. All business logic lives in service classes. Services are interface-backed (`IAccountService`, `ITransactionService`, etc.) and injected via ASP.NET Core's built-in DI container. O, L, and I are not actively designed for in Phase 1 — they emerge where genuinely needed.
- **Design patterns — Service Layer only for Phase 1:** One service class per feature area. Report generation lives in a single `ReportService` — the 4 Phase 1 report types are fixed and don't benefit from Strategy pattern overhead. If Phase 2 introduces dynamic or pluggable report types, Strategy can be introduced then. Factory is deferred to Phase 2+. Repository pattern is explicitly not used — EF Core's DbContext already provides that abstraction; layering a Repository on top adds boilerplate with no benefit.
- **Code structure — how a request flows:** `Controller` (HTTP only) → `IFeatureService` (injected) → `FeatureService` (business logic) → `DbContext` (EF Core, talks to PostgreSQL directly).
- **CSRF protection — ASP.NET Core anti-forgery, always on:** Every HTML form that triggers a state-changing action (POST) must include an anti-forgery token via ASP.NET Core tag helpers (`asp-` attributes on `<form>` elements generate the token automatically) and every corresponding controller action must carry `[ValidateAntiForgeryToken]`. ASP.NET Core's built-in anti-forgery middleware handles token generation and validation. This applies from the first form written in Phase 1 — it is not deferred.
- **Error handling — middleware + service-layer result signals:** A global exception handler is registered via `app.UseExceptionHandler("/Error")` in `Program.cs`. A dedicated `ErrorController` returns a plain error page in production; the developer exception page (`app.UseDeveloperExceptionPage()`) is used in development. Services signal expected failures (entity not found, validation error, business rule violation) by throwing typed domain exceptions — never by returning null. Controllers catch domain exceptions and redirect with a user-facing error message via `TempData`. Unexpected exceptions bubble up to the global handler.
- **Input validation — Data Annotations + ModelState:** Server-side validation uses ASP.NET Core's built-in Data Annotations on ViewModels (`[Required]`, `[Range]`, `[StringLength]`, etc.). Controllers check `ModelState.IsValid` before calling any service. Validation errors are displayed inline in Razor views via `<span asp-validation-for>` tag helpers and `<div asp-validation-summary>`. Client-side unobtrusive validation (included via ASP.NET Core's jQuery validation scripts) is acceptable for UX but is never the only layer — server-side is always authoritative.
- **ViewModel convention — dedicated ViewModels for all write operations:** Controllers never bind EF Core entity objects directly from POST requests and never pass entity objects to forms. Every create and edit form uses a dedicated ViewModel class (e.g. `AccountCreateViewModel`, `TransactionEditViewModel`). Read-only display views may use entities or lightweight read models directly. Controllers pass the ViewModel directly to the service, which does the manual mapping to entities — no AutoMapper in Phase 1.
- **Logging — built-in ILogger to console for Phase 1/2:** All classes that need logging receive `ILogger<T>` via DI. Log levels: `Information` for normal application events, `Warning` for unexpected but recoverable states, `Error` for exceptions with their full stack trace. In Phase 1/2, output goes to the console (ASP.NET Core default). Structured logging via Serilog and persistent log storage are deferred to Phase 3 when hosted deployment requires it. Audit logging (recording user actions for GDPR purposes) is a separate concern deferred to Phase 3 — see the Phase 3 section.
- **Database atomicity — use DbContext transactions for multi-step writes:** Any operation that writes to more than one table must be wrapped in an explicit database transaction. The critical example is account creation with an opening balance: the `Account` row and the opening balance `Transaction` row must both save or neither saves. Pattern: `await using var tx = await _db.Database.BeginTransactionAsync(); ... await tx.CommitAsync();` within the service method, with `await tx.RollbackAsync()` in the catch block. Do not rely on `SaveChangesAsync` alone when two separate entities must succeed or fail together.
- **Concurrency — last-write-wins accepted for Phase 1/2:** No optimistic concurrency tokens (`RowVersion`) are added in Phase 1 or 2. Two simultaneous form submissions could silently overwrite each other. This risk is accepted for local single-user use — it cannot occur in practice. Before Phase 3 (multi-user), a decision must be made on whether to add `RowVersion` to mutable entities (`Transaction`, `Transfer`, `Account`, `Category`). See Open Questions.

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
- **Opening balance prompt** — the account creation form includes a starting balance field (defaults to zero). On save, the app automatically creates an opening balance transaction for the entered amount, tagged to the system "Opening Balance" category. The user never has to construct this manually. The transaction is visible in history and editable if the amount was entered incorrectly.

**Starter Accounts**

| Account          | Type      | Notes                                            |
| ---------------- | --------- | ------------------------------------------------ |
| Cash             | Asset     | Physical cash on hand — petty cash, wallet money |
| Checking Account | Asset     | Main day-to-day bank account                     |
| Savings Account  | Asset     | General savings                                  |
| Credit Card      | Liability | General credit card placeholder                  |

### Transactions

- Record transactions with: date, amount, description, category, account
- Balance updates automatically per account

### Recurring Transaction Reminders

Recurring transactions are managed as a template (RecurringTransaction entity) that drives reminders — not auto-creation. The user confirms each reminder to generate a real Transaction record.

- User defines a recurring template: name, estimated amount, account, category, frequency (monthly/weekly/etc.), and day within the period
- The system computes `NextDueDate` from frequency and day; does not auto-advance — advances only when the user confirms or dismisses the reminder
- When a reminder is due, it surfaces as a dashboard notification (count of pending reminders due this month) and in a dedicated Reminders list
- Confirming a reminder opens the New Transaction form pre-filled from the template; the user reviews and saves it as a real transaction
- Dismissing a reminder advances `NextDueDate` without creating a transaction (e.g. user paid in cash, or the bill didn't arrive)
- Estimated amount is a hint, not enforced — the user edits the pre-filled amount before saving if the actual amount differs

### Categories

- User-defined categories for income and expenses
- Seeded with a default set on first run (see below) — users can add, rename, or delete any category

**Starter Income Categories**

| Category                    | Notes                              |
| --------------------------- | ---------------------------------- |
| Salary                      | Regular employment income          |
| Freelance & Self-employment | Independent work and consulting    |
| Investment Returns          | Dividends, interest, capital gains |
| Rental Income               | Income from property               |
| Business Income             | Revenue from a business            |
| Other Income                | Anything that doesn't fit above    |

**Starter Expense Categories**

| Category             | Notes                                      |
| -------------------- | ------------------------------------------ |
| Rent & Mortgage      | Monthly housing payment                    |
| Utilities            | Electricity, water, gas, internet, phone   |
| Groceries            | Supermarket and food shopping              |
| Dining & Restaurants | Eating and drinking out                    |
| Transportation       | Public transport, taxis, ride-sharing      |
| Fuel                 | Petrol / diesel for personal vehicle       |
| Healthcare & Medical | Doctor, pharmacy, dental, vision           |
| Insurance            | Health, home, car, life insurance premiums |
| Entertainment        | Cinema, concerts, hobbies, streaming       |
| Shopping & Clothing  | Apparel, household goods, personal items   |
| Education            | Courses, books, training                   |
| Subscriptions        | Software, memberships, recurring services  |
| Personal Care        | Haircut, gym, wellness                     |
| Travel               | Flights, hotels, holidays                  |
| Taxes & Fees         | Tax payments, bank fees, government fees   |
| Home & Maintenance   | Repairs, furniture, home improvement       |
| Other Expenses       | Anything that doesn't fit above            |

### Dashboard

Always live — no generation required. Updates automatically as transactions are entered.

- Current net worth (total assets − total liabilities)
- Total income vs. total expenses for the current month
- Simple account balance list per currency
- Savings rate for the current month ((Income − Expenses) ÷ Income), displayed as a percentage
- Count of pending recurring transaction reminders due this month

> Visual/graphical dashboard charts are planned for Phase 2. Phase 1 shows the same data as numbers only.

### Reports (Phase 1)

- Net Worth Statement
- Income & Expense Summary
- Expense Breakdown by Category
- Transaction History — requires pagination (strategy resolved, see ADR-0027 and Open Questions)

---

## Testing Strategy

### Stack

- **xUnit** — standard testing framework for .NET (used by ASP.NET Core itself)
- **Moq** — mocking library for isolating dependencies in unit tests
- **FluentAssertions** — makes test assertions read as plain English, cleaner than default xUnit assertions

### Types of Tests

| Type        | What it tests                                                | Database? | Introduced          |
| ----------- | ------------------------------------------------------------ | --------- | ------------------- |
| Unit        | Individual calculation or business logic method in isolation | No        | Phase 1             |
| Integration | EF Core queries and controllers against a real test database | Yes       | Phase 1 (as needed) |
| E2E         | Full browser-driven flows                                    | Yes       | Out of scope        |

E2E tests (Playwright, Selenium) are explicitly out of scope.

### What Gets Unit Tested (Priority Order)

1. **Financial calculations** — net worth, cash flow, account balance, currency-scoped totals.
2. **Business rules** — e.g. transfer source and destination must share the same currency, amount must be positive.
3. **Report query logic** — filters, date ranges, category/account scoping.

### What Gets Integration Tested

- EF Core queries that involve joins or aggregations (balance calculation, report queries)
- Multi-user data scoping in Phase 3 — tests that confirm one user cannot access another user's data

### Integration Test Database Strategy

- Dedicated local database: `project_ceres_test`
- EF Core applies migrations at the start of each test run via `_db.Database.Migrate()`
- Each test class wraps each test in a transaction that is rolled back after the test completes
- Connection string stored in `appsettings.Test.json` (gitignored) or via `dotnet user-secrets`

> **Testcontainers** deferred to Phase 3 when CI/CD pipelines are introduced.

---

## Open Questions / Decisions

### Unresolved — Phase 1


### Unresolved — blocks Phase 2

- [ ] **One transaction per budget goal** — `Transaction.BudgetId` is a single nullable FK. Users cannot link one payment to multiple goals. Options: leave the single-FK limit and document it, or replace BudgetId with a many-to-many `TransactionBudgetContribution` junction table. The junction approach is a schema change that affects every budget report query.

### Unresolved — blocks Phase 3

- [ ] **File attachment storage for Phase 3** — local filesystem does not scale to hosted multi-user. Must decide between cloud storage (Azure Blob Storage, S3) and server disk before Phase 3 begins. **Note: `StoredPath` will need a data migration if the storage backend changes after data exists — this decision affects Phase 1 schema.**
- [ ] **Timezone handling** — transaction dates stored as local date with no timezone. Must decide on a strategy before Phase 3: store UTC and convert for display, require users to set their timezone, or accept local-date ambiguity.
- [ ] **Authentication framework for Phase 3** — ASP.NET Core Identity, third-party provider (Auth0, Keycloak), or custom. Must be decided before any Phase 3 code is written.
- [ ] **Hosting platform for Phase 3** — Azure, AWS, DigitalOcean, Fly.io, or bare VPS. All other Phase 3 infrastructure decisions depend on this.
- [ ] **Email service for Phase 3** — no provider chosen (SendGrid, AWS SES, Mailgun, SMTP). Required before Phase 3 auth can launch.
- [ ] **Invite mechanism for Phase 3** — "invite-only beta" stated but not designed. No invitation entity, flow, or admin mechanism exists.
- [ ] **Multi-tenancy implementation** — adding UserId FK to every top-level entity. No migration plan exists. Must be designed before Phase 3 code begins.
- [ ] **Settings migration for Phase 3** — single Phase 1 Settings row becomes per-user. What do new users get as defaults?
- [ ] **WCAG 2.1 AA compliance (Phase 3)** — full audit required before opening to other users. EU Accessibility Act obligations may also apply — see legal.md.
- [ ] **MVC → Web API decoupling (Phase 3)** — no migration plan exists. Must be designed before Phase 3 frontend work begins.
- [ ] **Concurrency handling (Phase 3)** — last-write-wins accepted for Phase 1/2. Before Phase 3, decide whether to add EF Core optimistic concurrency tokens (`RowVersion`) to mutable entities.
- [ ] **Production migration strategy (Phase 3)** — `dotnet ef database update` vs. pre-deploy CI/CD step vs. reviewed SQL scripts. Option 2 or 3 recommended.
- [ ] **CI service for Phase 3** — no CI provider chosen. Required for automated vulnerability scanning and build verification before hosting.
- [ ] **Mobile app (Phase 3+)** — React Native is the leading candidate. No scope, timeline, or platform targets defined.

### Resolved (archived)

All resolved decisions (`[x]`) have been moved to [planning-resolved.md](planning-resolved.md) to keep this file lean. Decisions that produced ADRs are cross-referenced there.

---

## Next Steps

1. Install .NET SDK on macOS
2. Install PostgreSQL locally via Homebrew (`brew install postgresql@16`) or Postgres.app
3. Scaffold the ASP.NET Core MVC project
4. Define data models: Account, Transaction, Category
5. Set up Entity Framework Core with the PostgreSQL provider (Npgsql)
6. Build Controllers and Razor Views for each feature area
7. Wire up forms and navigation between pages
