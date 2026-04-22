# ADR 0036: ReportGeneratorFactory Introduced in Phase 2

## Status: Accepted

## Context

ADR-0017 established the Strategy pattern for report generation in Phase 1: each report
type is a class implementing `IReportGenerator`, and the selection logic (which generator
to use) lives in `ReportService`. ADR-0017 explicitly deferred a Factory to Phase 2+,
noting it should be introduced "only when the selection logic warrants it."

Phase 2 adds up to six new report types on top of the Phase 1 baseline:

- Net Worth Over Time
- Monthly Cash Flow Trend
- Spending by Category Over Time
- Year-over-Year Comparison
- Budget vs. Actual
- Largest Expenses

With six additions, the `switch`/`if-else` selection logic in `ReportService` grows to ten
or more branches. At that size, the selection logic becomes a maintenance concern of its
own — separate from the report generation logic it sits alongside. The condition ADR-0017
set for introducing a factory is met.

The decision is whether to introduce the factory before the first Phase 2 report generator
is written (proactive) or after the selection logic becomes painful (reactive).

## Decision

**`ReportGeneratorFactory` is introduced before the first Phase 2 report generator is
written.**

### Structure

```csharp
public interface IReportGenerator
{
    Task<ReportResult> GenerateAsync(ReportParameters parameters);
}

// One class per report type — unchanged from Phase 1 Strategy pattern
public class NetWorthReportGenerator : IReportGenerator { ... }
public class IncomeExpenseReportGenerator : IReportGenerator { ... }
// ... one per report type

// New in Phase 2 — owns the selection logic
public class ReportGeneratorFactory
{
    public IReportGenerator GetGenerator(ReportType type) => type switch
    {
        ReportType.NetWorth      => _netWorthGenerator,
        ReportType.IncomeExpense => _incomeExpenseGenerator,
        // one entry per type
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
```

Controllers and `ReportService` depend on `IReportGenerator` (via the factory), never on
concrete generator classes.

### SOLID properties satisfied

- **S (Single Responsibility):** Each generator class generates one report. The factory's
  sole responsibility is selecting the correct generator.
- **O (Open/Closed):** Adding a new report type requires one new class and one new line in
  the factory `switch`. No existing class is modified.
- **D (Dependency Inversion):** Consumers depend on `IReportGenerator`, not on any concrete
  implementation. The factory is the only place that references concrete classes.

### When to add a new generator

1. Create a new class implementing `IReportGenerator`
2. Register it in DI in `Program.cs`
3. Add one case to `ReportGeneratorFactory.GetGenerator`
4. Nothing else changes

## Consequences

**Positive:**
- Selection logic is isolated in one place — adding a report type has a known, minimal
  blast radius
- Each generator is independently unit-testable with no coupling to others
- Honours the decision deferred in ADR-0017 at the point it was intended to be executed
- Prevents a mid-phase structural refactor while simultaneously building report features

**Negative:**
- Small upfront cost: factory class must be written and wired into DI before it has more
  than one or two entries — it will look sparse initially
- One additional indirection layer compared to calling generators directly from `ReportService`
