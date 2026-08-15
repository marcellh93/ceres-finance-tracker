# ADR 0017: Application Architecture — SOLID Principles and Design Patterns

## Status: Accepted

## Context
Before writing any application code, the architectural approach for structuring services,
controllers, and business logic needed to be established. The goal was to pick a set of
principles and patterns that:
- Make the code testable (the project uses xUnit + Moq)
- Keep complexity proportional to the problem — no over-engineering for Phase 1 scope
- Are idiomatic for ASP.NET Core MVC

Four candidates were evaluated: SOLID principles, Service Layer, Strategy, Repository,
and Factory patterns.

## Decision

### SOLID — apply S and D, defer the rest

- **S (Single Responsibility):** Controllers handle HTTP only — request in, response out.
  All business logic lives in service classes. No logic in controllers.
- **D (Dependency Inversion):** All services are interface-backed and registered with
  ASP.NET Core's built-in DI container. Consumers depend on the interface, not the
  concrete class. This is what makes unit testing with Moq possible.
- **O, L, I:** Not actively designed for in Phase 1. These emerge naturally where needed
  and forcing them upfront leads to speculative abstractions.

### Design patterns in use

**Service Layer — Phase 1**
One service class per feature area (AccountService, TransactionService, CategoryService,
ReportService, etc.), each backed by an interface. Controllers call services; services
call DbContext.

**Strategy — Phase 1**
Report generation uses the Strategy pattern. Each report type implements a shared
`IReportGenerator` interface with its own query logic. Adding a new report type means
adding one new class — the controller and service that invoke it do not change.

```
IReportGenerator
  ├── NetWorthReportGenerator
  ├── IncomeExpenseReportGenerator
  ├── ExpenseBreakdownReportGenerator
  └── TransactionHistoryReportGenerator
```

**Factory — deferred to Phase 2+**
Only introduced when selecting a Strategy implementation at runtime becomes complex.
ASP.NET Core's DI handles simple cases (injecting a specific generator) without a factory.
A factory service is added only when the selection logic warrants it.

### Patterns explicitly not used

**Repository pattern — rejected**
EF Core's `DbContext` already is a Unit of Work and Repository. Adding a Repository layer
on top means two abstraction layers over the database with no practical benefit for this
project:
- Queries would need to be re-exposed through the Repository interface
- EF Core's LINQ composability would be lost or duplicated
- The project's integration tests use a real database — Repository abstraction is not
  needed to make tests work

Services call `DbContext` directly.

### Request flow

```
HTTP Request
    ↓
Controller          ← thin, handles HTTP only
    ↓
IFeatureService     ← interface, injected via DI
    ↓
FeatureService      ← business logic lives here
    ↓
IReportGenerator    ← Strategy interface (reports only)
    ↓
DbContext           ← EF Core, talks to PostgreSQL directly
```

## Consequences

**Positive:**
- Testable by design — Moq can mock any interface at the service layer
- Proportional complexity — no patterns added speculatively
- Idiomatic ASP.NET Core — follows the conventions the framework is built around
- Clear seam between HTTP handling and business logic from day one

**Negative:**
- Interface-per-service adds a small amount of boilerplate (interface file + implementation)
  — acceptable given the testability benefit
- Strategy pattern requires registering each generator in DI — minor setup cost per new
  report type
- Factory omission means the report selection logic lives in the service for now — if it
  grows complex, refactoring to a factory is straightforward but is a future task

## Enforcement (added 2026-08-15)

This ADR was convention-only for its whole life. A 2026-08-15 audit found 11 API
controllers calling `AppDbContext` directly, which produced the exact consequence
the ADR predicted: `SessionsApiController.BlockIp` inserted a `UserBlockedIp` and
bulk-revoked sessions in two separate statements with no transaction, and returned
500 on a duplicate block because nothing caught `UniqueConstraintViolationException`
(`SettingsService` shows the intended service-layer catch).

`CER007` now flags `AppDbContext` in `Controllers/`, at Info while the 30 remaining
read-path sites are migrated. The decision itself is unchanged — services own
business logic, controllers handle HTTP.
