# Decision Coach — Design Spec

> **Status:** Draft — pending user review
> **Diataxis type:** Explanation + reference — design decisions for a new Phase 3+ feature.
> **Phase:** Phase 3+. Sits after the SPA cutover and multi-tenancy work currently in flight. Adds a prerequisite refactor stage (extract `SpendableBalance` + build `LiabilityProjectionService`) before the Coach itself.

## Index

1. [Goal](#goal)
2. [Scope](#scope)
3. [Data model](#data-model)
4. [Coach architecture](#coach-architecture)
5. [Projection engine](#projection-engine)
6. [UI flow + conversion](#ui-flow--conversion)
7. [Testing strategy](#testing-strategy)
8. [Prerequisite refactor (Stage Coach.0)](#prerequisite-refactor-stage-coach0)
9. [Items deferred / flagged](#items-deferred--flagged)
10. [Open questions](#open-questions)

---

## Goal

Help the user decide whether to commit to a future financial obligation **before** signing for it. The user enters a pending decision (e.g. dentist's offer: €1,500 upfront + €180/month for 18 months), the system walks them through a guided wizard that captures need-vs-want / urgency / reversibility framing, projects their ledger forward, runs rule-driven evaluation, and returns a **verdict + dissent**. If the user decides to proceed, one click converts the scenario into the operational artifacts (a Liability account if financed, a savings Goal Budget if self-funded, or both).

The feature solves the **pre-commit gap**: existing tooling (amortising Liabilities, Spendable Balance, Goal Budgets) handles the post-commit world. Nothing today helps a user evaluate "should I sign this?" against their full forward financial picture.

The coach metaphor is deliberate: the user is "indecisive and wants to be told what to do." The wizard surfaces the structured thinking; the verdict commits to a recommendation; the dissent forces the user to confront the counter-argument before acting.

---

## Scope

### In scope (v1)

- Single-scenario decision support: one decision at a time.
- Deterministic, rule-based evaluation (no LLM).
- Decision journal: every scenario is saved with its frozen verdict and inputs.
- Conversion path from scenario → real ledger entities (Liability, Budget, or both).
- Per-scenario classification (`ScenarioCategory`) + per-app rule profile (`CoachRuleProfile`).
- 24-to-36-month forward projection with/without the commitment.

### Out of scope (v1)

- Multi-scenario comparison ("Option A vs Option B side-by-side"). Tracked as a future migration (Approach C from brainstorming).
- LLM-authored narrative recommendations.
- Per-user rule profile overrides (`UserCoachRuleProfile`).
- Auto-outcome-tracking ("did this actually play out as projected?"). The scenario is the origin record; the spawned ledger entities are the live source of truth.
- Stochastic / Monte Carlo simulation. Forecast is deterministic: today's pattern applied forward.
- Income variance, market growth modeling, shock modeling.
- Cross-currency scenarios. A scenario is single-currency, matching the existing app rule.
- Projection-result caching (recomputed every time). Materialization table flagged in `planning-future.md` if it becomes slow.
- Redis introduction. Discussed during brainstorming and deferred — see [Items deferred / flagged](#items-deferred--flagged).

### Forward-compatibility constraints

The C-migration ("multi-scenario plans" — debt-payoff plan, emergency-fund plan, etc.) is not built now, but the design preserves the option:

- **Engines accept a scenario as input, not the whole world.** `IProjectionService.ProjectAsync(scenario)` — no hidden coupling to "the one current decision."
- **No back-pointers from existing entities to `Scenario`.** FK direction is always Scenario → Account/Budget. Deleting the feature later is `DROP TABLE` + unwind two nullable FK columns; nothing else moves.
- **Three separable services.** `IProjectionService` (math), `ICoachAdvisor` (verdict assembly), `IScenarioConversionService` (artifact spawn). None depends on the others' internals.
- **Rules are pure functions over a DTO.** No DB calls, no clock, no service injection inside rules. Open/Closed: adding a rule is one class + one DI registration.

---

## Data model

Three new domain entities, three lookup-table-like entities (one is a join table). No changes to existing entities. All user-owned tables register in `UserOwnedTables.All` and inherit the RLS `user_isolation` policy from the Stage 7.5 work.

### Entity overview

```
ScenarioCategory       ──< Scenario  ──< ScenarioConversion >── Account  (spawned Liability)
                            │                              >── Budget   (spawned savings goal)
CoachRuleProfile       ──< Scenario                         (frozen Coach output stored on Scenario)
CoachRule              ──< CoachRuleProfileEntry >── CoachRuleProfile
CoachRule              ──< CashflowSafetyConfig
CoachRule              ──< GoalImpactConfig
CoachRule              ──< InterestCostConfig
CoachRule              ──< EmergencyBufferConfig
CoachRule              ──< DebtBurdenConfig
```

### `ScenarioCategory` — lookup, system-seeded

System-defined classification of "what kind of decision is this." Drives the default `CoachRuleProfile` selection in the wizard.

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | PK. Integer per the lookup-table convention. |
| `Code` | `varchar(40)` | `MedicalProcedure`, `BigPurchase`, `Travel`, `Education`, `HomeImprovement`, `Vehicle`, `Other` |
| `DisplayName` | `varchar(80)` | Localized via the resx pattern (see `Common/Localization`) |
| `DefaultProfileId` | `int` | FK → `CoachRuleProfile`. Recommended profile for this category. |
| `SortOrder` | `int` | UI ordering |
| `IsActive` | `bool` | System-defined; never hard-deleted (per `models.md` deletion rules) |

Seeded by migration. Users cannot create/delete in v1.

### `CoachRule` — lookup, system-seeded

One row per heuristic the Coach can evaluate. The row carries metadata; the **logic** lives in the matching C# class.

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | PK. Lookup table, integer per convention. |
| `Code` | `varchar(40)` | Stable kebab-case identifier — matches the C# class's `ICoachRule.Code` property. `cashflow-safety`, `goal-impact`, etc. **Once shipped, this string never changes.** |
| `DisplayName` | `varchar(80)` | "Cashflow safety" — what the UI surfaces |
| `Description` | `text` | One-sentence explainer shown when the user is browsing what each rule does |
| `IsActive` | `bool` | Global on/off. Inactive rules are skipped regardless of profile membership. |

Eight rows seeded in v1: `cashflow-safety`, `goal-impact`, `need-vs-want`, `reversibility`, `urgency`, `interest-cost`, `emergency-buffer`, `debt-burden`.

### `CoachRuleProfile` — lookup, system-seeded but editable

A named bundle: "which rules fire, with what severity weights, mapped to which verdict bands."

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `Code` | `varchar(40)` | `Conservative`, `Balanced`, `Aggressive` |
| `DisplayName` | `varchar(80)` | Shown in the wizard's profile-confirmation step |
| `Description` | `text` | One-paragraph explainer surfaced when the user is picking |
| `VerdictMapping` | `jsonb` | `{ "goAheadMaxScore": 4, "waitMaxScore": 9 }` — score-to-band thresholds. JSONB because the shape may evolve (e.g. adding a "PickAlternative" threshold later). |
| `Version` | `varchar(20)` | `"v1.0"` — bumps when the profile or its entries change. Captured into `Scenario.CoachRulesVersion` at decision time. |
| `IsActive` | `bool` | Soft-disable only. Historical scenarios reference profiles by FK and version snapshot. |
| `CreatedAt` / `UpdatedAt` | `timestamptz` | Standard |

### `CoachRuleProfileEntry` — join table

Per-profile membership of rules, with weights. One row per (profile, rule) pair.

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `CoachRuleProfileId` | `int` | FK → `CoachRuleProfile` |
| `CoachRuleId` | `int` | FK → `CoachRule` |
| `IsEnabled` | `bool` | Allows a profile to turn a rule off without removing the row (preserves history of "this profile used to use this rule") |
| `SeverityWeight` | `int` | How much this rule's severity counts toward the verdict score, in this profile |

`UNIQUE(CoachRuleProfileId, CoachRuleId)` — a rule appears at most once per profile.

### Per-rule configuration tables

One config row per rule that has tunable parameters. Three v1 rules (`need-vs-want`, `reversibility`, `urgency`) read only the scenario's own tags and have **no config table at all**.

#### `CashflowSafetyConfig`

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `CoachRuleId` | `int` | FK → `CoachRule`, `UNIQUE` (one config per rule) |
| `FloorAmountFlat` | `decimal(18,2)`, nullable | Flat-amount floor (e.g. €500) |
| `FloorMultiplierOfExpenses` | `decimal(5,4)`, nullable | Multiplier of the user's average monthly expenses (e.g. 1.0 = "one month of expenses") |

`CHECK` constraint: exactly one of `FloorAmountFlat` / `FloorMultiplierOfExpenses` is non-null. The rule's C# code reads both, picks whichever is set, and runs the matching branch.

#### `GoalImpactConfig`

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `CoachRuleId` | `int` | FK → `CoachRule`, `UNIQUE` |
| `MaxSlipMonths` | `int` | Maximum acceptable slip in any active goal's hit-date (e.g. 2 = "up to 2 months is OK") |

#### `InterestCostConfig`

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `CoachRuleId` | `int` | FK → `CoachRule`, `UNIQUE` |
| `HighRatePct` | `decimal(5,4)` | Annual interest rate above which the rule escalates (e.g. 0.10 = 10%) |
| `TotalInterestPctCap` | `decimal(5,4)` | Total interest as percentage of principal above which the rule escalates (e.g. 0.15 = 15%) |

#### `EmergencyBufferConfig`

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `CoachRuleId` | `int` | FK → `CoachRule`, `UNIQUE` |
| `MinMonthsOfExpenses` | `int` | Required emergency buffer in months of average expenses (e.g. 3) |

#### `DebtBurdenConfig`

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | PK |
| `CoachRuleId` | `int` | FK → `CoachRule`, `UNIQUE` |
| `MaxDebtToIncome` | `decimal(5,4)` | Maximum acceptable monthly-debt-service / monthly-income ratio (e.g. 0.35) |

### `Scenario` — main domain entity

The decision artifact. Stores inputs, picks a category + profile, and freezes the Coach's output at decision time.

| Field | Type | Notes |
|---|---|---|
| `Id` | `uuid` | PK — user-facing in URLs |
| `UserId` | `uuid` | FK → `ApplicationUser`. Implements `IUserOwned`. Registered in `UserOwnedTables.All`; inherits RLS `user_isolation` policy. |
| `Name` | `varchar(120)` | "Braces — Option A (Invisalign)" |
| `CurrencyId` | `int` | FK → `Currency`. Scenario is single-currency. |
| `ScenarioCategoryId` | `int` | FK → `ScenarioCategory`. Required at creation. |
| `CoachRuleProfileId` | `int` | FK → `CoachRuleProfile`. Defaulted from `ScenarioCategory.DefaultProfileId`, user-overridable. |
| **Decision inputs** | | |
| `UpfrontAmount` | `decimal(18,2)` | Day-one outflow. `≥ 0`. |
| `MonthlyAmount` | `decimal(18,2)` | Recurring outflow. `≥ 0`. |
| `TermMonths` | `int` | Length of monthly commitment. `0` = upfront-only. |
| `InterestRatePct` | `decimal(5,4)`, nullable | Annual rate. `NULL` = unknown or zero-financing. |
| `StartDate` | `DateOnly` | When the upfront hits / monthlies begin. |
| `NeedOrWant` | `enum`/`varchar(20)` | `Need` / `Want` / `Mixed` |
| `Urgency` | `enum`/`varchar(20)` | `Now` / `Within3Months` / `Within12Months` / `Discretionary` |
| `Reversibility` | `enum`/`varchar(20)` | `Reversible` / `PartiallyReversible` / `Irreversible` |
| `Rationale` | `text` | Free-text, required, min 1 char |
| `SourceAccountId` | `uuid`, FK → `Account`, nullable | The asset account the upfront amount would come from. Nullable while `Status=Draft`; required before "Decide & convert" can be clicked. Currency must match `Scenario.CurrencyId` (app-level CHECK). |
| `MonthlyOriginAccountId` | `uuid`, FK → `Account`, nullable | The account monthlies would leave from. Same nullable + currency-match semantics. Often equals `SourceAccountId`. |
| **Coach output (frozen at decision time)** | | |
| `Verdict` | `enum`/`varchar(20)`, nullable | `GoAhead` / `Wait` / `DoNotProceed` / `PickAlternative`. Null while `Status=Draft`. |
| `VerdictReason` | `text`, nullable | Rendered reasoning (concatenation of supporting rules' `Reason` strings). |
| `Dissent` | `text`, nullable | Counter-argument the advisor derived from the highest-severity opposing rule. Empty when no rule opposed the verdict. |
| `CoachRulesVersion` | `varchar(20)`, nullable | Snapshot of `CoachRuleProfile.Version` at decision time. Verdicts are never silently re-evaluated when rules change. |
| **Lifecycle** | | |
| `Status` | `enum`/`varchar(20)` | `Draft` / `Decided` / `Converted` / `Abandoned` |
| `DecidedAt` | `timestamptz`, nullable | Set when user transitions out of `Draft` |
| `CreatedAt` / `UpdatedAt` | `timestamptz` | Standard |
| `DeletedAt` | `timestamptz`, nullable | Soft delete — restorable, per `SavedReport` precedent |

### `ScenarioConversion`

Records the link from a scenario to the operational artifact(s) it spawned when the user clicked "Convert."

| Field | Type | Notes |
|---|---|---|
| `Id` | `uuid` | PK |
| `ScenarioId` | `uuid` | FK → `Scenario`. `UNIQUE` — one conversion per scenario. |
| `SpawnedAccountId` | `uuid`, FK → `Account`, nullable | Spawned Liability (financed branch) |
| `SpawnedBudgetId` | `uuid`, FK → `Budget`, nullable | Spawned savings goal Budget (self-funded branch) |
| `ConvertedAt` | `timestamptz` | When the conversion happened |

`CHECK`: at least one of `SpawnedAccountId` / `SpawnedBudgetId` is non-null.
`UserId` is derived through the FK to `Scenario`; the row implements `IUserOwned` for RLS via the join.

### Deletion rules (consolidated)

Follows the conventions in `docs/models.md`:

| Entity | Rule | Reason |
|---|---|---|
| `ScenarioCategory` | Never deletable — system-defined | Same as `AccountType`, `CategoryType` |
| `CoachRule` | Never deletable; `IsActive=false` to disable | Historical scenarios reference rule codes via profile snapshots |
| `CoachRuleProfile` | Never hard-delete; `IsActive=false` to retire | Historical scenarios reference by FK |
| `CoachRuleProfileEntry` | Soft-disable via `IsEnabled=false` | Preserves history of profile composition |
| `*Config` (per-rule) | Never deletable independently | Lifecycle tied to parent `CoachRule` |
| `Scenario` | Soft delete (`DeletedAt`) — restorable | Decision history is a one-way ratchet |
| `ScenarioConversion` | Cascades from `Scenario` soft-delete | Tied 1:1 to its scenario |

### What does NOT change

- `Account`, `Budget`, `Transaction`, `Transfer`, `Settings`, `RecurringTransaction`, `LiabilityPayment`, `Movement` schemas are untouched.
- **No back-pointers to `Scenario` from existing entities.** FK direction is always Scenario → Account/Budget.

---

## Coach architecture

Three jobs, three services, each replaceable. Code lives under `ProjectCeres/Services/Coach/` and `ProjectCeres/Services/Projection/` — flat-with-subdirectory layout matching the existing `Services/Reports/` precedent. **No `Application/` layer is introduced** — the project convention is services flat under `ProjectCeres/Services/`.

### The three jobs

| Job | Lives in | What it does |
|---|---|---|
| **Projection** | `IProjectionService` (`Services/Projection/`) | Takes the user's accounts, recurring transactions, goals, and a scenario. Returns a month-by-month forecast — with and without the commitment. No advice, just numbers. |
| **Rule evaluation** | Individual rule classes (`Services/Coach/Rules/`) | Each rule looks at the projection + scenario tags + its own config row, produces one `RuleResult` (severity + reason + direction). No knowledge of other rules, no DB calls. |
| **Verdict assembly** | `ICoachAdvisor` (`Services/Coach/`) | Calls projection, runs enabled rules, scores them by `SeverityWeight` from the profile, picks one of `GoAhead` / `Wait` / `DoNotProceed` / `PickAlternative`, writes the dissent from whichever rules pulled the opposite way. |

### Folder layout

```
ProjectCeres/
  Services/
    Coach/
      ICoachAdvisor.cs           ← public entry point
      CoachAdvisor.cs            ← orchestrator: runs rules, picks verdict, writes dissent
      ICoachRule.cs              ← interface every rule implements
      RuleResult.cs              ← DTO: severity + reason + pull direction + ruleCode
      CoachInput.cs              ← DTO bundle handed to each rule
      CoachOutput.cs             ← DTO: verdict + reason + dissent + rulesVersion
      Rules/
        CashflowSafetyRule.cs    → Code = "cashflow-safety"
        GoalImpactRule.cs        → Code = "goal-impact"
        InterestCostRule.cs      → Code = "interest-cost"
        EmergencyBufferRule.cs   → Code = "emergency-buffer"
        DebtBurdenRule.cs        → Code = "debt-burden"
        NeedVsWantRule.cs        → Code = "need-vs-want"
        ReversibilityRule.cs     → Code = "reversibility"
        UrgencyRule.cs           → Code = "urgency"
    Projection/
      IProjectionService.cs      ← public interface; no Coach dependency
      ProjectionService.cs
      ProjectionResult.cs
```

### The rule contract

Every rule is the same shape: a class with a stable code and one method that produces a finding.

```csharp
public interface ICoachRule
{
    string Code { get; }                          // "cashflow-safety" — matches CoachRule.Code
    RuleResult Evaluate(CoachInput input);
}
```

A rule is a **pure function** over `CoachInput`. No database access, no clock, no service injection. Everything it needs is in the input bundle. The advisor populates the bundle once before iterating; rules cannot reach outside.

### What `CoachInput` carries

```csharp
public class CoachInput
{
    public Scenario Scenario { get; init; }            // the decision under evaluation
    public ProjectionResult Projection { get; init; }  // 24-36 months of forecast, with/without
    public RuleConfig Config { get; init; }            // this rule's own config row (e.g. CashflowSafetyConfig)
}
```

The `Config` is a discriminated payload — each rule knows the concrete type it expects (`CashflowSafetyConfig`, `GoalImpactConfig`, etc.). Rules with no config table receive `null` here and read only `Scenario` + `Projection`.

### What `RuleResult` returns

```csharp
public class RuleResult
{
    public string RuleCode { get; init; }       // "cashflow-safety" — for traceability
    public Severity Severity { get; init; }     // None / Green / Yellow / Red
    public string Reason { get; init; }         // user-facing sentence
    public VerdictPull Pull { get; init; }      // TowardGo / TowardWait / TowardStop
}
```

Three properties matter:

- **Severity** is bounded: four values. Lets the advisor score consistently.
- **Reason** is a complete user-facing sentence. The rule (not the advisor) decides what to say about itself.
- **Pull** is the rule's direction (not strength — that's severity). How the advisor knows which rules "won" and which "lost" when building the dissent.

### What `CoachAdvisor` does

```
1. Resolve the profile from the scenario   → which rule codes are enabled, with what weights
2. Call IProjectionService                 → get the with/without forecast
3. For each enabled rule:                  → load its config row, build CoachInput, call Evaluate(), collect RuleResult
4. Score the results                       → sum (severity × weight); map to verdict via the profile's VerdictMapping
5. Build the dissent                       → find the highest-severity rule pulling opposite to the verdict; surface its Reason
6. Snapshot CoachRulesVersion              → write CoachRuleProfile.Version into Scenario.CoachRulesVersion
```

~80 lines of C#. No business judgment of its own — every opinion lives in a rule class or a config row.

### Severity-to-score mapping

| Severity | Score |
|---|---|
| `None` | 0 |
| `Green` | 0 (the rule did fire but found nothing of concern in this direction) |
| `Yellow` | 2 |
| `Red` | 5 |

Score contribution per rule = `severityScore × CoachRuleProfileEntry.SeverityWeight`.

For each `VerdictPull` direction (`TowardGo`, `TowardWait`, `TowardStop`), sum the contributions. The dominant direction's score is compared against `CoachRuleProfile.VerdictMapping`:

- `TowardStop` sum ≥ `waitMaxScore` (or any single Red `TowardStop` rule) → `DoNotProceed`
- `TowardWait` sum > `goAheadMaxScore` and ≤ `waitMaxScore` → `Wait`
- Otherwise → `GoAhead`

`PickAlternative` is reserved for the multi-scenario future and is never emitted by v1.

### Worked example — dentist scenario

User enters: €1,500 upfront + €180/month for 18 months, no interest. Tags: `Need + Now + Irreversible`. Category: `MedicalProcedure` → defaults profile to `Conservative`.

1. **Projection** runs. Returns: monthly Spendable Balance dips to €280 in month 4 (without scenario it would be €460). Active "House deposit" goal slips by 1 month. Total interest €0.
2. **CashflowSafetyRule** sees the projected €280 vs. the €500 floor (from `CashflowSafetyConfig.FloorAmountFlat`). Returns `Severity=Yellow, Pull=TowardWait, Reason="Spendable Balance dips to €280 in month 4 — below your €500 floor."`
3. **GoalImpactRule** sees 1-month slip vs. 2-month tolerance. Returns `Severity=Green, Pull=TowardGo, Reason="House deposit goal slips by 1 month — within your tolerance."`
4. **InterestCostRule** sees 0% rate. Returns `Severity=None`.
5. **NeedVsWantRule** sees `Need`. Returns `Severity=None, Pull=TowardGo, Reason="Classified as a Need."`
6. **ReversibilityRule** sees `Irreversible`. Returns `Severity=Yellow, Pull=TowardWait, Reason="Decision is irreversible — wrong call is hard to undo."`
7. **UrgencyRule** sees `Now`. Returns `Severity=Green, Pull=TowardGo, Reason="Marked urgent — waiting carries a cost."`
8. **EmergencyBufferRule**, **DebtBurdenRule** — both `Green`.

**Scoring** (assume Conservative weights: cashflow=5, reversibility=2, goal=3, urgency=1):

- `TowardWait` = (2 × 5) + (2 × 2) = 14
- `TowardGo` = (0 × 3) + (0 × 3) + (0 × 1) = 0
- `TowardStop` = 0

With `Conservative.VerdictMapping = { goAheadMaxScore: 4, waitMaxScore: 9 }`, `TowardWait=14` exceeds both — lands in `Wait` band.

**Verdict: Wait.**

**Dissent**: opposing direction to Wait is `TowardGo`. Among `TowardGo` rules, `GoalImpactRule` (Green) and `UrgencyRule` (Green) tie on severity; `NeedVsWantRule` is `None`. Tiebreak rule for the advisor: prefer the rule with the **largest absolute score contribution** (severity × weight) — that's `GoalImpactRule` at `0 × 3 = 0` vs. `UrgencyRule` at `0 × 1 = 0`, still tied. Final tiebreak: rule with the higher `CoachRuleProfileEntry.SeverityWeight` — `GoalImpactRule` wins (weight 3 vs urgency weight 1). Advisor surfaces `GoalImpactRule.Reason`: *"House deposit goal slips by 1 month — within your tolerance."*

The advisor may optionally concatenate the top N opposing reasons (configurable in `VerdictMapping`, default N=1). When N=1, the dissent is one rule's `Reason`; when N=2+, the advisor joins them with a separator and prefixes "But — counter-arguments:". The mocked verdict screen above shows the N=2+ shape for narrative readability; v1 ships with N=1 (single dissent) and the screen renders the one `Reason` directly.

### How adding/refining a rule works

- **Tune a rule's threshold** — edit the relevant `*Config` row. No deploy.
- **Add a heuristic** — write a new rule class, register in DI, add `CoachRule` row, add `*Config` table if parameters needed, list the code in whichever profiles should use it via `CoachRuleProfileEntry`. No existing class touched.
- **Refine an existing rule** — edit one class. Its tests live next to it.
- **Decommission a rule** — `CoachRule.IsActive = false`. Historical scenarios still reference the rule by code via their `CoachRulesVersion` snapshot.

---

## Projection engine

Answers a narrow question: "if the user signs this scenario, what does their financial life look like month by month for the next N months — and what does it look like if they don't?"

### What it produces

For each month in the horizon, two numbers per metric: **with** the scenario, **without** it.

| Metric | Definition | Source |
|---|---|---|
| **Spendable Balance** | Non-excluded asset balances minus that month's expected recurring transactions (same definition as Phase 2 dashboard) | Reusable `ISpendableBalanceCalculator` (extracted by Stage Coach.0) iterated month by month |
| **Net Worth** | Assets − Liabilities at end of month | Account balances projected forward by applying recurring transactions and the new commitment if "with" |
| **Active goal progress** | For each active savings goal: linked account's projected balance vs. target | Linked account's projected balance |
| **Total monthly debt service** | Sum of all monthly debt outflows (existing amortising Liabilities + scenario's monthly if financed) | `ILiabilityProjectionService` (newly built in Stage Coach.0) + scenario's monthly amount |

Returned as `ProjectionResult`: arrays of `MonthPoint` for "without" and "with," plus a summary header (currency, start date, horizon).

### Horizon

| Scenario shape | Horizon |
|---|---|
| `TermMonths > 0` | `max(TermMonths + 6, 24)` |
| `TermMonths == 0` | 24 months |

The "+6 months of recovery" lets the goal-impact rule see whether goals catch back up after the commitment ends.

### What "without" is

Baseline forecast — the user's current trajectory:

- Current account balances at `StartDate`
- Recurring transactions applied forward at their cadence
- Existing amortising Liabilities' payoff schedules (from `ILiabilityProjectionService`)
- Active goals tracked against their linked accounts

### What "with" adds

Baseline plus:

- `UpfrontAmount` debited at `StartDate` from `Scenario.SourceAccountId`
- `MonthlyAmount` debited from `Scenario.MonthlyOriginAccountId` each month for `TermMonths`
- Interest cost computed if `InterestRatePct` is set, using the same amortisation math as `ILiabilityProjectionService`

The projection treats the financed scenario **as if** an amortising Liability existed. It doesn't actually create one — the Liability materializes when the user clicks Convert.

### Implementation shape

```csharp
public interface IProjectionService
{
    Task<ProjectionResult> ProjectAsync(Scenario scenario, CancellationToken ct);
}
```

Inside, it composes the reusable engines:

```
ProjectionService
  ├── Reads user's accounts, recurring transactions, goals, amortising liabilities
  ├── For each month in the horizon:
  │     ├── Calls ISpendableBalanceCalculator       — gets baseline for that month
  │     ├── Calls ILiabilityProjectionService       — gets debt schedule for that month
  │     ├── If "with scenario": layers upfront/monthly/interest on top
  │     └── Records both numbers
  └── Returns ProjectionResult
```

### `ProjectionResult` shape

```csharp
public class ProjectionResult
{
    public int CurrencyId { get; init; }
    public DateOnly StartDate { get; init; }
    public int HorizonMonths { get; init; }
    public IReadOnlyList<MonthPoint> WithoutScenario { get; init; }
    public IReadOnlyList<MonthPoint> WithScenario { get; init; }
}

public class MonthPoint
{
    public int MonthOffset { get; init; }
    public DateOnly MonthEnd { get; init; }
    public decimal SpendableBalance { get; init; }
    public decimal NetWorth { get; init; }
    public decimal TotalMonthlyDebtService { get; init; }
    public IReadOnlyList<GoalProgressPoint> Goals { get; init; }
}

public class GoalProgressPoint
{
    public Guid BudgetId { get; init; }
    public string GoalName { get; init; }
    public decimal ProjectedBalance { get; init; }
    public decimal Target { get; init; }
    public DateOnly? ProjectedHitDate { get; init; }   // null if outside horizon
}
```

Each `MonthPoint` is self-contained. Rules can query any month without looking at neighbours.

### Caching

**None in v1.** Projections recomputed every time they're needed. Acceptable because:

- A single projection is bounded (~36 months × ~10 goals = a few hundred numbers).
- User-scoped, scenario-specific; would need invalidation on every Account/Transaction/Recurring/Goal change.
- Compute cost is the EF reads to load the user's ledger.

If projections become slow once users have hundreds of recurring transactions, materialize as `ScenarioProjectionPoint` rows keyed by a hash of scenario + ledger version. **Flagged in `planning-future.md`. Not built now.**

### What the projection does NOT do

- **No income variance prediction.** Recurring transactions assumed unchanged.
- **No market growth modeling.** Investment accounts project flat unless recurring transactions are set up on them.
- **No shock modeling.** No "what if your car breaks down" Monte Carlo. The emergency-buffer rule covers this conceptually.
- **No source-account suggestion.** The wizard asks the user (Section 6).

Deliberate — the forecast is "today's pattern, applied forward, plus this commitment." Predictable enough that users understand it. The Coach's rules surface gaps without us pretending we modeled them.

---

## UI flow + conversion

Three modes: drafting (wizard), reviewing the verdict, and converting.

### Mode 1 — Wizard (drafting)

Multi-step, single focus per step. Each step auto-saves to the draft `Scenario` row so the user can leave and resume.

| Step | What it asks | What it captures |
|---|---|---|
| 1. What are you deciding? | Name + category picker | `Name`, `ScenarioCategoryId` (selects default profile) |
| 2. The numbers | Upfront, monthly, term, interest rate (optional), start date, currency | `UpfrontAmount`, `MonthlyAmount`, `TermMonths`, `InterestRatePct`, `StartDate`, `CurrencyId` |
| 3. Where's the money coming from? | Source asset account (upfront) + origin account (monthlies) | `SourceAccountId`, `MonthlyOriginAccountId` |
| 4. Why are you considering this? | Free-text rationale | `Rationale` |
| 5. The decision shape | Three guided questions: Need vs Want? Urgency? Reversibility? | `NeedOrWant`, `Urgency`, `Reversibility` |
| 6. Confirm the profile | Recommended profile with description; user can change | `CoachRuleProfileId` |
| 7. Review and get the verdict | Read-only summary + "Get verdict" button | Triggers Coach evaluation; goes to Mode 2 |

### Validation rules (step-by-step)

- **Step 2:** `UpfrontAmount ≥ 0`, `MonthlyAmount ≥ 0`, `TermMonths ≥ 0`, `InterestRatePct` ∈ [0, 1] if set, `StartDate ≥ today`. At least one of upfront / monthly must be `> 0`. Failures use the project's auto-422 `ValidationProblemDetails` shape (`code: "VALIDATION_ERROR"`, `details[]`, `traceId`).
- **Step 3:** source/origin account currencies must match `Scenario.CurrencyId`. Cross-field violation → 422 `UnprocessableEntity` with the project's dual-shape error contract (per `api-contract.md` § 4).
- **Step 4:** `Rationale` required, min 1 char.

### Why a wizard, not a single form

The user is "indecisive and wants to be told what to do." A long form puts every field on screen at once and asks the user to assemble the picture themselves. A wizard surfaces one decision at a time, in an order the system controls — and the very act of going through it **is** the structured thinking the coach metaphor promises. The wizard is the coaching, before the verdict is even produced.

### Mode 2 — Verdict screen

Single page, top-to-bottom:

```
┌──────────────────────────────────────────────────────────┐
│  Braces — Option A (Invisalign)                          │
│  €1,500 upfront + €180/month for 18 months · Medical     │
├──────────────────────────────────────────────────────────┤
│                                                          │
│                       VERDICT: Wait                      │
│                                                          │
│  Why:                                                    │
│  • Spendable Balance dips to €280 in month 4 — below     │
│    your €500 floor (Cashflow safety: caution)            │
│  • Decision is irreversible — wrong call is hard to undo │
│    (Reversibility: caution)                              │
│                                                          │
│  But — counter-argument:                                 │
│  Marked as a Need with high urgency, and your House      │
│  Deposit goal would only slip by 1 month. If waiting     │
│  means losing the dentist's slot or paying more later,   │
│  Wait has real cost.                                     │
│                                                          │
├──────────────────────────────────────────────────────────┤
│  Projection (next 24 months)        [with] [without]     │
│  ▁▂▂▃▄▄▅▅▆▆▇▇▇▇▆▆▅▅▄▄▃▂▁▁  Spendable Balance            │
│  ▆▆▆▆▆▆▇▇▇▇████▇▇▇▆▆▆▅▅▄▃  Net Worth                    │
│  ──────────────────────────────  Goal: House Deposit     │
│                  ↑ would hit in May 2027 (with)          │
│                  ↑ would hit in April 2027 (without)     │
├──────────────────────────────────────────────────────────┤
│  [ I'll decide later ]  [ Abandon ]  [ Decide & convert ]│
└──────────────────────────────────────────────────────────┘
```

Three properties:

1. **Verdict is the headline, dissent is the second paragraph.** Not equal-weighted — system commits first, then immediately surfaces what could be wrong about its own commitment.
2. **Projection is secondary.** User can toggle with/without and hover for month numbers, but it sits below the verdict. Verdict is the answer; chart is the receipt.
3. **Three actions, three lifecycles:**
   - "I'll decide later" → `Status=Draft`, verdict cached on the row.
   - "Abandon" → `Status=Abandoned`, kept in journal as a no-go.
   - "Decide & convert" → Mode 3.

### Mode 3 — Conversion

The scenario becomes operational. Branch by scenario shape.

#### Branch A — Financed (`MonthlyAmount > 0 AND TermMonths > 0`, with or without `InterestRatePct`)

System proposes creating a new amortising Liability account mirroring the scenario. On confirm:

- New `Account` row, `AccountTypeId=Liability`, `LiabilityRepaymentType=Amortising`, `InterestRate` from scenario.
- New `Transaction` for the upfront amount, sourced from `SourceAccountId`.
- New `RecurringTransaction` for the monthly payment, linked to the new Liability + `MonthlyOriginAccountId`.
- New `ScenarioConversion` row linking scenario → spawned Account.
- `Scenario.Status = Converted`, `DecidedAt` set.

#### Branch B — Self-funded (`MonthlyAmount=0 AND TermMonths=0 AND UpfrontAmount > 0`)

Pure cash purchase, possibly with a savings goal. System proposes creating a `Budget` with `GoalType=Savings`, `LinkedAccountId` = a savings account chosen by the user, `TargetAmount = UpfrontAmount`.

- New `Budget` row.
- New `ScenarioConversion` row linking scenario → spawned Budget.
- `Scenario.Status = Converted`.

#### Branch C — Hybrid (`UpfrontAmount > 0 AND MonthlyAmount > 0`)

Both happen. `ScenarioConversion` has both FKs populated.

#### After conversion

The spawned entities are the live source of truth going forward. The scenario stays in the journal as the **origin record** — frozen, browseable, never the live source for an active commitment. Editing the spawned Liability later does NOT modify the scenario (verdicts are audit artifacts).

### SPA routes

```
/coach                          ← decision journal listing
/coach/new                      ← wizard, step 1
/coach/{id}/step/{n}            ← wizard, deeplinkable to a specific step
/coach/{id}/verdict             ← Mode 2
/coach/{id}/convert             ← Mode 3 (confirmation page before the actual write)
```

The journal lists scenarios filtered by status, with verdict, category, and links to spawned Account/Budget if `Converted`.

### React client

Built with the existing stack — React 19 + Vite + TypeScript, Tailwind v4, shadcn/ui `base-nova` style. Per `CLAUDE.md` § Frontend Work: `frontend-design` and `vercel-react-best-practices` invoked at component design; `web-design-guidelines` audit before commit; UX/UI verification checklist run in the browser before stage close-out.

Primitives reused from the design system:

- Tabular numerics via `<Numeric>` (no inline `font-mono`).
- KPI tiles via `<StatTile>` / `<EquationRow>`.
- Status indicators via `<Badge variant="success|warning|info">` (no hand-rolled `bg-{x}/10 text-{x}`).
- Toasts via `sonner`.
- shadcn primitives use base-ui under the hood — `<PopoverTrigger render={...}>` not Radix `asChild`; `<MenuItem onClick>` not `onSelect`.

---

## Testing strategy

High-stakes feature: rule typos produce **plausible-looking wrong financial advice**. Strategy designed around catching that class. Follows `docs/testing.md` (TDD required, integration tests hit real Postgres, no mocked DB, marker-filtered queries to avoid cross-test bleed).

### Layer 1 — Rule unit tests (per rule)

Rules are pure functions over `CoachInput`. Tests build synthetic `CoachInput` and assert `RuleResult`. No DB, no fixtures.

Each rule must have:

- **Green case** — clearly safe input.
- **Yellow case** — borderline input.
- **Red case** — clearly unsafe input.
- **None case** — rule not applicable (e.g. `InterestCostRule` with `InterestRatePct=NULL`).
- **Boundary case at every threshold** — for every parameter, one test exactly at, one below, one above. This is the **`<` vs `<=` test** — the bug class that produces silent verdict-flips on edge values.
- **Negative-assertion test per parameter** — assert explicitly what the rule does **not** do at given values. Pins the "tests-as-ship-gate" memory rule for wrapped framework behavior.

Eight rules × ~10 tests each → ~80 fast unit tests.

### Layer 2 — Projection integration tests

Real DB (`IntegrationTests` collection, marker-filtered).

- Cash upfront only: with/without diverges by exactly `UpfrontAmount` in month 0, then parallel.
- Monthly payments, no interest: with/without diverges by `MonthlyAmount × monthsElapsed` linearly.
- Monthly payments with interest: assert schedule matches `ILiabilityProjectionService` output for an equivalent Liability — single source of truth between projection and the post-commit world.
- Scenario crossing a recurring transaction boundary: recurring still applies in the "with" timeline.
- Scenario crossing a goal's projected hit date: `GoalProgressPoint.ProjectedHitDate` shifts correctly.
- Horizon edge cases: `TermMonths=0 → 24 months`, `TermMonths=18 → 24 months (max of 24, 24)`, `TermMonths=30 → 36 months`.

~10 tests.

### Layer 3 — Advisor integration tests

Tests the scoring + dissent assembly, not the rules themselves.

- All Green → `GoAhead`, dissent empty.
- One Red, others Green → verdict reflects Red's `Pull`.
- Mixed signals → verdict matches `VerdictMapping` bands.
- **Dissent comes from the highest-severity opposing rule.** Construct case where two rules pulled opposite (one Green, one Yellow); assert Yellow's `Reason` surfaces.
- No opposing rule → dissent empty (no fabrication).
- Disabled rules don't count — disable via `CoachRuleProfileEntry.IsEnabled=false`, assert verdict unchanged.
- `CoachRulesVersion` captured at decision time.

~8 tests.

### Layer 4 — Profile + config integration tests

Proves the rule-config-via-data layer actually works.

- Same scenario, Conservative profile → Wait; same scenario, Aggressive profile → GoAhead. Different verdicts, same inputs.
- Editing `CashflowSafetyConfig.FloorAmountFlat` flips the verdict on the same scenario. Asserts configs are read at runtime, not hardcoded.
- Adding a `CoachRuleProfileEntry` for a previously-disabled rule produces a new finding in `VerdictReason`.

~6 tests.

### Layer 5 — Conversion integration tests

The highest-stakes write in the feature.

- Branch A: new Liability has correct `InterestRate` + `LiabilityRepaymentType=Amortising`; `RecurringTransaction` created; upfront `Transaction` created from `SourceAccountId`; `ScenarioConversion` links scenario → account; `Status=Converted`.
- Branch B: new `Budget` of `GoalType=Savings`; conversion row links scenario → budget.
- Branch C: both spawned; conversion row has both FKs.
- **Idempotency**: clicking convert twice returns an error (the `UNIQUE(ScenarioId)` constraint), not a duplicate Liability.
- **RLS scoping**: User A cannot convert User B's scenario; cannot pick User B's source account. Asserts via the parity test pattern from Stage 7.5.
- **Editing the spawned Liability does NOT modify the scenario.** Pins "verdict is an audit artifact."

~10 tests.

### Architectural tests (NetArchTest)

- **The projection does not mutate the ledger.** Run a projection, then re-query — assert nothing changed.
- **No rule class references `AppDbContext`, `DbSet<>`, or any repository.** Pins "rules are pure."
- **`Services/Coach/` references `IProjectionService` (interface only), not `ProjectionService`.** Boundary enforcement.
- **Verdict cannot be produced on `Status=Draft`** without going through the wizard's explicit trigger.
- **Soft-deleted scenarios excluded from journal listing** but queryable via restore.

~5 tests.

### Total

~120 tests. Significant, but commensurate with the stakes.

---

## Prerequisite refactor (Stage Coach.0)

The Coach cannot ship until two reusable engines exist. Today neither does:

- **`SpendableBalance` is a private method `GetSpendableBalanceAsync(int currencyId)` inside `DashboardService`**, returning a 5-tuple for dashboard rendering. Not a reusable engine.
- **`LiabilityProjectionService` does not exist in code.** `planning-phase2.md` documents `ILiabilityProjectionService` as if shipped, but a grep confirms no implementation. The closest is `LiabilityPaymentService`, which handles payment writes, not amortisation schedules.

### Stage Coach.0 deliverables

1. **Extract `ISpendableBalanceCalculator`** from `DashboardService`:
   - Interface accepting `(int currencyId, DateOnly asOfMonth, Guid userId)` — month parameter is **new**, used by the Coach's projection loop; the dashboard caller passes today's month and gets identical output to pre-refactor.
   - Implementation in `ProjectCeres/Services/SpendableBalanceCalculator.cs`.
   - `DashboardService.GetSpendableBalanceAsync` becomes a thin caller of the new service. Behavior unchanged for the dashboard.
   - Tests cover the per-month iteration, not just "today."

2. **Build `ILiabilityProjectionService` / `LiabilityProjectionService`** (truly new, despite the Phase 2 doc framing):
   - Pure math, no DB access (matches the Phase 2 spec intent).
   - Accepts a Liability account's principal, interest rate, monthly amount, term.
   - Returns the amortisation schedule (per-month: principal paid, interest paid, remaining balance).
   - Used by the existing Liability account ledger page (currently builds its own projection inline — refactored to call the service) and by the Coach's projection engine.

3. **Update `planning-phase2.md`** to mark Liability projection as **deferred to Stage Coach.0** rather than implemented. Add the corresponding `[ ]` close-out item to the receiving stage per the "deferral requires a checkbox in the receiving stage" rule.

### Stage Coach.0 sequencing

Coach.0 must close before Stage Coach.1 (data model migrations) begins. The Coach feature's downstream work depends on both engines being callable.

---

## Items deferred / flagged

These need entries written into the durable planning docs as part of accepting this spec:

### `planning-future.md`

1. **Auto-outcome tracking for scenarios** — "did this play out as projected?" Not in v1. Requires a job that compares the frozen projection vs. realized ledger N months later.
2. **Multi-scenario plans (Approach C)** — debt-payoff plans, emergency-fund plans, side-by-side comparison. The current data model preserves the option (engines accept a scenario as input; no back-pointers from existing entities); building it requires a new `FinancialPlan` aggregate that orchestrates `ICoachAdvisor` calls.
3. **Per-user rule profile overrides (`UserCoachRuleProfile`)** — if users want their own thresholds independent of the three system profiles.
4. **Projection materialization (`ScenarioProjectionPoint`)** — Postgres table caching projection points, keyed by hash of scenario + ledger version. Only if recompute becomes a bottleneck.
5. **Redis introduction (broader-than-Coach decision)** — when Project Ceres needs a second app instance (horizontal scaling, availability, blue-green deploys), Redis comes in. Migrates the in-memory rate-limit, MFA challenge state, email idempotency keys. Not on the Coach's critical path.

### `planning-phase3.md`

- Add Coach.0 + Coach feature as a new Stage line under the appropriate Batch (likely a new Batch after current Batch 5, since current Phase 3 stages are auth/multi-tenancy/SPA cutover focused).
- Cross-reference to this spec.

### `roadmap-phase-three.md`

- Add the matching `[ ]` checklist entries for Coach.0 (extract `SpendableBalanceCalculator`, build `LiabilityProjectionService`, refactor Liability ledger page to use it, dashboard regression-test) and Coach (data model migrations, services, wizard, verdict screen, conversion, journal, ~120 tests).

---

## Open questions

1. **Phase positioning.** Spec assumes Coach lands as a new Batch after current Phase 3 batches close, but does not commit to a stage number. Confirm with user where it fits — could be Phase 3 final batch, or Phase 4.
2. **`SourceAccountId` validation timing.** Currently required before "Decide & convert" can be clicked. Open: should the wizard reject `Draft → Decided` if source/origin accounts are missing, or block at convert time only?
3. **Localization scope for `ScenarioCategory.DisplayName` and `CoachRule.DisplayName`.** Phase 3 has the resx pattern in place (Stage 8b). Confirm whether v1 ships English-only or with EN + ES at first ship.
4. **NetArchTest dependency.** Architectural tests assume NetArchTest (or equivalent). If not already in the project, adding it is a small build dependency to weigh against pure-code-review enforcement.
5. **`PickAlternative` verdict.** Reserved in the enum for the multi-scenario future but never emitted by v1. Alternative: omit from the enum until C-migration lands and add it then. Trade-off: schema stability vs. forward signaling.
