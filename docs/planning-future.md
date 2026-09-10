# Project Ceres — Phase 4 & 5 Planning

---

## Phase 4 — Freelancer / Autónomo Support

**Gate: Phase 3 (auth + hosting) must be in place.**

Goal: extend the app to serve freelancers and autónomos specifically.

| Feature                                                   | Why it matters for autónomos                                                                                                           |
| --------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| **Personal vs. Business flag on Accounts and Categories** | Separates personal and business activity for tax-purpose reports                                                                       |
| **IVA / VAT tracking**                                    | Track IVA collected and IVA paid separately for quarterly Hacienda filings                                                             |
| **Investment holdings tracking**                          | Individual positions (ticker/name, units, purchase price, current price, unrealized gain/loss). Prices updated manually, no live feed. |
| **Investment income tracking**                            | Dividends and capital gains recorded as transactions in the investment account                                                         |
| **Quarterly tax summary (Modelo 130 / 303)**              | Report summarizing taxable income and IVA figures per quarter                                                                          |
| **Client tracking**                                       | Associate income transactions with specific clients                                                                                    |
| **Invoice reference on transactions**                     | Link a transaction to an invoice number for traceability                                                                               |
| **Bank connectivity**                                     | Automatic transaction import via open banking (Nordigen/GoCardless for Spanish banks under PSD2)                                       |

---

## Open Questions (Phase 4)

- [ ] **Business vs. personal tagging — schema and UX design** — The feature table lists "Personal vs. Business flag on Accounts and Categories" but no design decisions have been made. Key open questions: (1) Is it a binary flag or a multi-value tag (e.g. personal / business / mixed)? (2) Does it apply only to Accounts and Categories, or also to individual Transactions? (3) How does the income/expense report present mixed-use accounts — split proportionally, show both, or require explicit per-transaction tagging? Autónomos in Spain who use the same bank account for personal and professional activity will need a clear answer to (3) before this feature is useful. No decisions made.

---

## Phase 4 additions

### Net worth milestones

A `NetWorthMilestone` entity: name, target amount, currency, target date (optional), notes (optional). Displayed on the dashboard as a progress bar comparing current derived net worth against the target. Distinct from Goal Budgets — a Goal Budget tracks spending toward a purpose; a net worth milestone tracks overall financial position against a long-term target (e.g. "reach €50k net worth by December 2027"). No new movement types or category tagging required — reads the same derived net worth calculation already used in reports.

Gate: only meaningful after several months of net worth history exist. Do not introduce at Phase 3 launch.

### Spending velocity alerts

A mid-month proactive notification: "You've spent 90% of your [Category] budget with 15 days remaining." Triggered by the existing `CategoryBudget` data. Threshold and delivery channel (email, push) are user-configurable. Opt-in — disabled by default.

Implementation note: shares a background job (`FinancialNotificationJob`) with the weekly digest email introduced in Phase 3. The same scheduled pass that assembles the digest evaluates CategoryBudget thresholds and queues alerts for any user who has both opted in and crossed the threshold since the last check. Building both features on a single job runner avoids setting up the scheduler twice.

Gate: requires the Phase 3 email service and background job infrastructure to be stable first.

---

### Financial Snapshot Service

A server-side service (`IFinancialSnapshotService`) that computes the authoritative current financial state for a user at a point in time. This is the shared data layer that both the Projection Engine and the Insight Engine read from — neither engine queries transactions directly.

**Snapshot shape:**
```
FinancialSnapshot
- NetWorth              (derived: assets − liabilities)
- SpendableSummary
  - AvailableNow        (liquid balances − bills due within 7 days)
  - SafeToSpend         (AvailableNow − budget reserved)
  - WarningLevel        (None | Low | Medium | Critical)
- BudgetStatus[]        (per CategoryBudget: allocated, spent, remaining, % used)
- UpcomingRecurring[]   (next 30 days of recurring transactions)
- SavingsRate           (income − expenses / income, trailing 3 months)
```

`WarningLevel` is derived from `SafeToSpend`: `None` (> 20% of budget reserved), `Low` (0–20%), `Medium` (zero), `Critical` (negative). This drives the Safe to Spend alert introduced in Phase 3 and future insight severity.

The snapshot is computed on demand (not stored as columns) — consistent with the existing derived-value-only principle. Background jobs read a snapshot at run time; dashboard API calls compute one per request. If performance becomes an issue, cache per user with a short TTL (5 minutes).

Gate: design the snapshot service contract before building the Insight Engine — both depend on it.

---

### Projection and Insights Engine

A forward-looking analysis layer that generates personalised spending projections and actionable recommendations based on historical patterns. Seeded by the Phase 3 Safe to Spend alert (the MVP safety net for when spending headroom hits zero). Reads from `FinancialSnapshot`, never directly from transaction tables.

**Insight types — three distinct categories:**

| Type | Purpose | Examples |
|------|---------|---------|
| `Warning` | Alert to a problem requiring action | "Safe to Spend is negative", "Food budget 90% used with 15 days left" |
| `Opportunity` | Surface a positive action the user could take | "You have €340 unallocated this month — enough to top up your emergency fund" |
| `Progress` | Communicate improvement to build confidence | "You saved €320 more than last month", "Spending decreased 12% vs. last quarter", "On track for your Japan trip goal" |

`Progress` insights are the monetisation layer — users pay for clarity and confidence, not for CRUD. Making improvement visible and measurable is the primary value differentiator.

**Core projection capabilities:**
- **Spending projection:** given current-period velocity and days remaining, project budget overshoot. "At your current pace you will exceed Food by €120 this month."
- **Safe to Spend runway:** project how many days the current Safe to Spend value will last at the user's average daily spending rate.
- **Anomaly detection:** flag transactions significantly above the user's typical amount for that category. "This €340 electricity bill is 2.4× your usual — is this correct?"
- **Seasonal pattern recognition:** recognise recurring cost spikes (annual insurance renewals, quarterly IVA payments, holiday spending) and warn ahead of time.
- **Personalised recommendations:** one actionable insight surfaced per week on the dashboard. "Transport costs increased 40% over 3 months — the largest driver is fuel."

**Decision impact layer (Phase 5+):**
Before recording a large transaction, the user can ask "what does this cost me?" The system projects the downstream impact:
- "This purchase reduces your savings rate from 25% → 18%"
- "You will delay your Japan trip goal by 2 months"
- "After this, Safe to Spend drops to €43 for the rest of the period"

This requires the Projection Engine to run a forward simulation from the current snapshot with the proposed transaction applied. Not a Phase 4 feature — requires the snapshot and insight infrastructure to be stable first.

**Implementation approach:**
- All projections derived from existing transaction data — no external data sources, no ML in Phase 4.
- Rule-based heuristics over transaction history are sufficient for all listed Phase 4 capabilities.
- Insights stored in an `Insight` table: `(Id, UserId, Type, Message, GeneratedAt, DismissedAt?)`. Background job generates insights on a schedule; shares `FinancialNotificationJob` infrastructure from Phase 3.
- Dashboard surfaces the most recent undismissed insight per type. Users can dismiss individually.
- Unified data flow: `Transactions → FinancialSnapshot → Projection Engine → Insight Engine → UI`

Gate: requires at least 3 months of transaction history per user for meaningful projections. New users see an onboarding prompt ("Add 3 months of history to unlock spending insights") instead of empty insight cards.

---

### Auto-Categorisation (Rules-Based)

Post-import intelligence that automatically assigns categories to imported transactions based on configurable rules, reducing manual categorisation work after CSV import.

**Rule model:**
- Rules are user-defined: if transaction description contains `[keyword]`, assign category `[X]`
- Rules can also match on amount range, account, or direction (income/expense)
- Rules are ordered — first match wins
- Rules are stored per user in a `CategoryRule` table: `(Id, UserId, MatchField, MatchValue, CategoryId, Priority)`
- Rules are applied at import time, before the staging/review step — not retroactively to existing transactions
- The user can override any auto-assigned category during the import review step

**Duplicate detection via fingerprinting:**
For CSV imports where no unique transaction ID exists (unlike OFX's `FITID`), use a content fingerprint: `HASH(date + amount + description + accountId)`. Before persisting a staged import row, check the fingerprint against existing transactions. Flag probable duplicates in the import review UI — the user confirms or discards, never auto-discarded.

Gate: requires the Phase 3 import UX redesign (staging + review step) to be stable. Do not add auto-categorisation until the import flow is solid.

---

### Household / Collaboration Model

Shared finances for couples, flatmates, and family units. A high-impact architectural change — do not implement until Phase 3 multi-tenancy is stable and real usage data shows demand.

**V1 (low architectural cost):**
- Shared read-only access: a user can grant another user read-only access to their data (specific accounts or all)
- Shared report export: generate a combined report across two users' accounts for a specific date range
- No shared data ownership — each user still owns their own data

**V2 (full household model — major architectural change):**
Replace `UserId` ownership with `HouseholdId` on all entities. Add `HouseholdMember` table with roles (Owner, Member). Scope all queries to household context. This touches every service, every query, and every IDOR test — it is not a feature, it is a data ownership decision that must be planned as a dedicated migration.

Gate: defer V2 until Phase 5+ and only if V1 proves insufficient for real usage patterns. V1 can be shipped in Phase 4 with minimal architectural disruption.

---

### Value Feedback Loop

The system must communicate improvement explicitly — users do not perceive value from data alone. They pay for clarity, confidence, and visible progress toward better outcomes.

**Implementation via `Progress` insight type (see Projection and Insights Engine):**
- Period-over-period comparisons: "You saved €320 more than last month"
- Trend summaries: "Spending decreased 12% vs. last quarter"
- Goal progress: "You are on track for your Japan trip — 67% funded"
- Savings rate tracking: displayed on dashboard as a trailing 3-month metric; improving trend surfaced as a `Progress` insight

**Guiding product principle:**
> At every step, ask: "Does this help the user decide something?" If not, it is likely not part of the core product differentiation.

Value is not in features. Value is in making improvement visible and measurable.

---

### Strategic Positioning Note

The competitive angle for Project Ceres is **low-friction financial visibility** — not bank sync speed, not feature count. The target evolution is from a financial tracking system to a **financial decision-making and life-planning platform**.

The app should not compete on bank connectivity (expensive, unreliable, PSD2-heavy). It should compete on:
- The quality of insight derived from the data the user already has
- The reduction of cognitive load ("How much can I spend?" answered immediately)
- Making improvement visible so users feel the value of continued use

---

## Maybe / Future Consideration

These ideas have merit but are not assigned to a phase yet. Revisit when the app is in daily use.

### Server-side Reminder + Review count providers

**Goal:** when the reminders feature and the review surface need a server-side row count (currently the SPA computes both client-side), add `ReminderCountProvider` and `ReviewCountProvider` services that wrap a UserId-scoped EF query.

*(Moved from Stage 7 close-out 2026-05-12: the Stage 7 service-audit checklist enumerated these two providers as future audit targets. Neither exists in the codebase yet because the SPA computes both counts client-side via the existing list endpoints. When either is added, the service must inject `ICurrentUserAccessor` and chain `.Owned(user)` on its DbSet query — same pattern as every other Stage 7-audited service. The Stage 7 EF global query filter on the underlying entity is the second safety net regardless.)*

### Native mobile apps (iOS and Android)

**Goal:** full native mobile experience — not just a mobile-optimised website.

Two separate client projects:
- **iOS** — Swift + SwiftUI (or UIKit where needed). Submitted to the App Store.
- **Android** — Flutter or Java/Kotlin. Submitted to Google Play. Flutter is worth evaluating as a shared codebase option between iOS and Android, which would reduce maintenance burden significantly at the cost of some platform-native feel.

**Backend impact:** the Phase 3 Web API (ASP.NET Core, pure JSON) is already the correct architecture for mobile clients. No additional backend work is required beyond ensuring the API surface is complete and versioned. JWT authentication (already planned for Phase 3) is the right credential mechanism for mobile — cookies are browser-only.

**PWA as a stepping stone:** a Progressive Web App (manifest + service worker on top of the React SPA) can be installed to the home screen and offers limited offline read access. It is not a replacement for native apps — it has no access to native device features (biometrics, push notifications, widget API, system keychain) and is not distributed via the App Store or Google Play. A PWA can be shipped earlier as an interim solution while native apps are in development, but it does not remove the need for them.

**Open questions — resolve before committing to native:**
- [ ] Flutter vs. separate Swift + Android native: Flutter reduces codebase surface but introduces a Dart dependency and a framework layer. Separate native apps give full platform fidelity but double the client maintenance cost. Decision depends on available development capacity.
- [ ] Which Phase 3 features ship in v1 of the mobile app vs. which are web-only initially? A scoped mobile v1 (dashboard, transactions, transfers, account balances) is faster to ship than a full feature-parity client.
- [ ] Push notification infrastructure: APNs for iOS, FCM for Android. This is separate from the email service and requires additional setup. Spending velocity alerts and session notifications are the first candidates for push delivery.
- [ ] Biometric authentication: Face ID / Touch ID on iOS, fingerprint on Android. Users expect this from a financial app. Requires the platform keychain — not available in a PWA.

**Gate:** Phase 3 Web API must be stable and versioned before mobile development begins. Do not start mobile clients against a moving API target.

### Receipt scanning and line-item tracking

Upload a receipt image (or scan via phone) → an LLM parses it → the transaction total is auto-filled and line items are stored as structured metadata linked to the transaction.

**Data model sketch:**
- `Transaction` → many `ReceiptLineItems`: `StoreName`, `ProductName`, `Quantity`, `UnitPrice`, `LineTotal`, `ParsedAt`
- Receipt image lives in `TransactionAttachment` (already planned)

**What it enables (personal history only, no cross-user data):**
- Itemized breakdown per shopping trip
- Your own price trend on a given product over time (e.g. olive oil cost history across receipts)
- Budget breakdown by item if line items are tagged

**Known limitations / open questions:**
- Product name normalization is hard — same item prints differently across stores and receipts
- Value only materializes after months of consistent use
- LLM parsing adds cost and a network dependency — conflicts with the local-first Phase 1 ethos
- Without cross-user data, market comparison ("Mercadona vs. Carrefour for this basket") is not possible

**Gate:** Only worth considering after Phase 3 (auth + multi-user) is stable, and only if the app is in consistent daily use.

### TanStack Query (server-data caching layer)

A drop-in `useQuery`/`useMutation` library that wraps `fetch` and adds: cross-component cache by key, stale-while-revalidate, refetch on tab focus, retry with exponential backoff, request deduplication, and mutation-driven cache invalidation. Industry standard for React data fetching; ~13KB gzipped + a `QueryClientProvider` at the root.

**Why not now:** the Phase 3 SPA pages each fetch independent data with no overlap. The `useApi` hook in `src/app/lib/use-api.ts` covers the loading/data/error/refetch lifecycle for one component at a time, which is all we need.

**Adopt when any of these become true:**
- Two pages share a data source (e.g. a sidebar unread-count and a dashboard unread-count both hit the same endpoint — should share one cache, not double-fetch).
- We add mutations that should invalidate related queries across pages (e.g. creating a transaction should refresh the dashboard, the Transactions page, and the Movements page in one declaration).
- We have 5+ pages with their own ad-hoc fetch state and the boilerplate starts wearing thin.
- We want optimistic updates or offline reads.

Migration path: introduce `QueryClientProvider` in `src/app/main.tsx`, replace `useApi` call sites with `useQuery` one page at a time. The two patterns coexist during migration.

---

### Settings-aware formatting

- **What:** A `useSettings()` hook + `formatDate` / `formatMonth` / `formatNumber` / `formatPercent` utils that respect the user's `DateFormat` and `NumberFormat` preferences from `Settings`.
- **Why:** SPA currently formats dates/numbers with locale-default `Intl` calls. The Razor side already uses `NumberFormatHelper` for amounts and respects `Settings.DateFormat` / `Settings.NumberFormat`. The SPA needs parity before settings UI ships.
- **Scope:** Cross-cutting — touches charts (axis labels, tooltips), KPI cards, transaction lists, reports. Should land as one coordinated change rather than per-feature drift.
- **Triggered by:** Dashboard Phase 2 spec (`docs/superpowers/specs/2026-04-29-dashboard-phase-2-design.md`, §9 Out of Scope).

---

### Movements filter — Show: All / Active accounts only

- **What:** A filter chip on the SPA Movements page (`/app/movements`) letting the user restrict the list to transactions whose account is currently active. Defaults to All so the audit-trail behaviour is preserved.
- **Why:** When the Accounts SPA shipped (2026-05-03), we deliberately kept Movements showing transactions for archived accounts too — hiding them would conflict with the trust/auditability promise (you should always be able to find a transaction you remember entering) and with the deletion-strategy ADR (ADR-0023). But once a user has accumulated several archived accounts, the noise may become real. Add the toggle when that friction shows up in actual use.
- **Scope:** Single chip in `MovementsFilterBar.tsx`; URL param `accountStatus=active`; server-side filter on the existing `/api/movements` endpoint or post-filter in the SPA (decide at implementation time based on dataset size).
- **Triggered by:** Accounts SPA design discussion (`docs/superpowers/specs/2026-05-03-accounts-spa-design.md`, Q4 follow-up).

---

### Dashboard net-worth tile — "Includes archived accounts" tooltip

- **What:** A small info tooltip on the Net Worth KPI tile (and the Net Worth Over Time chart) that explains: archived accounts still contribute to the figure because the underlying money or debt still exists. Same wording the Accounts archive AlertDialog uses.
- **Why:** Per `models.md` line 276–281, deactivated accounts must count toward net worth and balance reports — otherwise the figure would silently drop when an account is archived. This is correct behaviour but non-obvious. The tooltip closes the loop so a user doesn't see net worth fail to drop after archiving and lose trust in the math.
- **Scope:** One Tooltip + Info icon next to the Net Worth tile title in `ProjectCeres.Client/src/app/features/dashboard/NetWorthCard.tsx`; same treatment on the Net Worth Over Time chart header (`NetWorthChart.tsx` in the same directory).
- **Triggered by:** Accounts SPA design discussion (`docs/superpowers/specs/2026-05-03-accounts-spa-design.md`, Q4 follow-up).

---

### Session-validation per-request DB write — performance review

> **PARTIALLY RESOLVED in Stage 6b.3 (2026-05-10):** `SessionRevocationValidator` now skips the `LastUsedAt` UPDATE if `session.LastUsedAt > now − 60 seconds` (60-second debounce). This eliminates write amplification on rapid-fire authenticated GETs without weakening the revocation guarantee (the SELECT still runs on every request). The remaining options below (in-memory cache, drop-write, hand-rolled SQL) are still valid paths if the debounce proves insufficient at scale.

- **What:** Re-evaluate the `SessionRevocationValidator` cost model. Today it runs `OnValidatePrincipal` on every authenticated request: `SELECT` the `UserSession` row by `SessionId`, validate `RevokedAt`, write back `LastUsedAt = DateTime.UtcNow` (debounced to max once per 60s, Stage 6b.3), `SaveChangesAsync`. That is one round-trip per authenticated request — independent of whether the request is a 1ms idempotent GET or a heavy mutation. The `LastUsedAt` write is now debounced.
- **Why still tracked:** the SELECT still runs per request. At scale the read amplification may still matter if connection pool saturation precedes WAL pressure.
- **Remaining mitigations to consider when revisiting (in order of preference):**
  1. **In-memory cache + periodic flush.** Cache `(SessionId → UserSession)` in `IMemoryCache` keyed by SessionId, evict on `RevokedAt` set or on session end. Flush `LastUsedAt` to DB every 60s instead of per request. Preserves "active sessions" UI granularity. Loses some accuracy if the app crashes between flushes (acceptable — `LastUsedAt` is informational, not security-critical).
  2. **Drop the per-request `LastUsedAt` write entirely.** Recompute idle expiry from the Identity ticket's `IssuedUtc` claim, not from the DB. Sacrifices the "last seen" granularity in the active-sessions UI. Cheapest in DB load.
  3. **Hand-rolled SQL.** Replace EF read+write with a single `UPDATE "UserSessions" SET "LastUsedAt" = NOW() WHERE "Id" = @id AND "RevokedAt" IS NULL RETURNING 1` — bypasses EF change-tracking overhead but keeps the per-request round-trip.
- **What is NOT acceptable as a "fix":** weakening the revocation guarantee. The reason `OnValidatePrincipal` reads from the DB on every request is precisely so a logged-out session is rejected within milliseconds, not eventually. Any optimisation must preserve "logout takes effect on next request."
- **Triggered by:** Stage 6a design (`docs/superpowers/specs/2026-05-09-stage-6a-identity-foundation-design.md`, §4 SessionRevocationValidator).
- **Gate:** revisit when production traffic justifies measurement, or when request-latency profiling shows the validator as a top-N consumer. **Not a Phase 3 blocker.**

---

### Receivables — money owed to the user, with optional interest accrual

**What:** A first-class way to track "someone owes me money" — symmetric to the existing Liability + amortising-interest construct, but mirrored: interest accrues on what's owed *to* the user, the borrower's payments reduce the balance, and the user can review an aging schedule of outstanding receivables. Closes a real gap in the data model where the only workaround today is an Asset account with a "Loan to X" description and manually recorded Income entries — which loses automatic interest accrual and conflates receivables with cash.

**Why this matters for the autónomo audience:** late-paying clients with penalty interest under Spanish commercial law (Ley 3/2004) are a daily reality for Spanish freelancers. Informal personal loans to family or friends with agreed interest are common across the broader audience. The existing model handles "you owe someone" symmetrically well (Liability + LiabilityPayment + Amortising); the missing mirror means "someone owes you" can only be modelled as an unaccrued asset balance, which understates the true position over time.

**Architectural sketch (open questions inside):**

- **Account modelling:** three forks. (a) Add `IsReceivable: bool` to Asset accounts and reuse the existing `LiabilityRepaymentType` + `InterestRate` fields — minimum schema change, but couples a sub-flag to AccountType in a way that complicates queries ("show me real assets" needs a where-not clause). (b) Introduce a third `AccountType` row called `Receivable` — clean separation, but the lookup table currently asserts a binary "Asset / Liability" world that touches reports, dashboard, charts, and the existing balance-sign convention. (c) Introduce a separate `Receivable` entity entirely, FK'd to its own ledger, not modelled as an Account at all — most flexible but reinvents the most-tested code path in the app.
- **Interest accrual mechanism:** two forks. (a) Periodic background job recomputes accrual at period close, writing an `InterestAccrual` ledger entry — auditable, deterministic, but introduces scheduled background work to an app that today is purely on-read. (b) On-read recomputation — every time the user opens the receivable's ledger, derive accrued interest from the principal + rate + elapsed periods. Avoids new infrastructure but breaks the audit-trail principle ("when did this interest hit my balance" becomes "the moment you happened to refresh"). The choice here is an ADR-level decision because it's the first scheduler the app would have.
- **Collection events:** mirror of `LiabilityPayment`. Reuse the same entity with sides swapped (cleanest for code, awkward for field semantics — `AssetAccountId` and `LiabilityAccountId` would stop matching their meaning), or introduce `ReceivableCollection` as a parallel entity (clean semantics, doubled code paths). A fourth movement type appears in the unified Movements list and quick-add modal regardless.
- **Surfaces that need re-audit:** net worth math (already covers receivable principal but not accrued interest), Movements list (interest accruals are a fourth row type), spendable balance (receivables should default to `ExcludeFromSpendable=true` because uncollected money isn't spendable), Reports (an aging-schedule report is a standard accounting expectation), Dashboard (an "Owed to me" tile is a likely add).

**Why deferred (not now):**
- Substantial new server work (entity, service, accrual logic, possibly a scheduler) that lives outside the current SPA-migration framing. The Accounts SPA scope is already large (CRUD + Ledger + payoff projection + conditional liability fields + adaptive archive copy + server policy hardening); folding receivables in blows it up.
- The accrual scheduler decision is ADR-grade — it's the first scheduled background work in the codebase. Needs its own brainstorm to settle on-read vs. cron, miss-handling, audit semantics.
- Receivables touch sensitive multi-user data (who owes whom). Phase 4 (auth + multi-user) is the right gating context, both for the audience fit (autónomos) and for the privacy implications.

**Workaround until shipped:** Create an Asset account named "Loan to X", set the opening balance to the principal, record collection events as Income transactions tagged to a "Loan repayment" category. Loses automatic interest accrual but principal tracking works.

**Gate:** Phase 4 (freelancer/autónomo support). Open the brainstorm once the Accounts SPA, the broader SPA migration (Batches 2 + 3 + cleanup), and Phase 3 auth are stable.

**Triggered by:** Accounts SPA brainstorm (`docs/superpowers/specs/2026-05-03-accounts-spa-design.md`, post-Q12 follow-up question from the user, 2026-05-03).

---

### Decision Coach — affordability simulation for pending commitments

**What:** A guided pre-commit decision-support feature. The user enters a pending financial obligation (e.g. "dentist's offer: €1,500 upfront + €180/month for 18 months"), a wizard walks them through need-vs-want / urgency / reversibility framing, the system projects their ledger forward 24–36 months **with and without** the commitment, runs rule-driven evaluation, and returns a **verdict + dissent** (e.g. "Verdict: Wait — but the decision is irreversible and marked urgent, so waiting has real cost"). If the user decides to proceed, one click converts the scenario into the operational artifacts (a new amortising Liability account if financed, a savings goal `Budget` if self-funded, or both).

**Why it matters:** The current system solves the post-commit world well — amortising Liability accounts project payoff schedules, Spendable Balance shows today's headroom, Goal Budgets track savings. None of them help the user evaluate **whether to commit at all** against their full forward picture. The brainstorming session that produced the spec started from a real moment the user had after a dentist visit: enough cash to start, salary to cover the monthlies, no tool in the app to decide whether saying yes was financially responsible.

**Design source of truth:** [`docs/superpowers/specs/2026-05-16-decision-coach-design.md`](superpowers/specs/2026-05-16-decision-coach-design.md). That document is the full spec — entities, rule architecture, projection engine, UI wizard, conversion path, testing strategy. The bullets below are a high-altitude summary so this entry is greppable without opening the spec.

**Shape, in brief:**

- New entities: `Scenario` (the decision artifact, with frozen verdict + dissent), `ScenarioConversion` (link from scenario to spawned Account/Budget), `ScenarioCategory` (lookup), `CoachRuleProfile` + `CoachRuleProfileEntry` (which rules fire, with what weights), `CoachRule` (lookup, one row per heuristic), and one config table per parameterised rule (`CashflowSafetyConfig`, `GoalImpactConfig`, `InterestCostConfig`, `EmergencyBufferConfig`, `DebtBurdenConfig`).
- Rule logic stays in code (typo-proof, unit-testable); rule **parameters** live in their own typed-column tables (one per rule), tunable without a deploy. Three of the eight v1 rules read only the scenario's tags and have no config table.
- Three separable services: `IProjectionService` (math), `ICoachAdvisor` (verdict assembly), `IScenarioConversionService` (artifact spawn). No back-pointers from existing entities to `Scenario` — FK direction is always Scenario → Account/Budget, so the feature is removable.
- React SPA wizard at `/coach`, journal listing of all scenarios with their verdicts, verdict screen with a projection chart, conversion confirmation.

**Prerequisite — Stage Coach.0:** The spec depends on two reusable engines that **do not exist in code today**:
- `ISpendableBalanceCalculator` must be extracted out of `DashboardService.GetSpendableBalanceAsync` (currently a private 5-tuple method serving only the dashboard).
- `ILiabilityProjectionService` must be built (despite `planning-phase2.md` documenting it as if shipped — verified missing from the codebase 2026-05-16).
Coach.0 ships as the first sub-stage of the Coach batch. Not pulled forward into Phase 3 — Phase 3 is already long and the gap has been latent for months without harm.

**Why deferred (not now):**
- Significant new server work (~120 tests, multiple migrations, a wizard, conversion logic) sitting outside Phase 3's auth/multi-tenancy/SPA-cutover framing.
- Depends on the SPA being stable and on Phase 3's auth + multi-tenancy being live (both already shipped through Stage 7.5, but the Coach UI fit-and-finish work assumes the SPA shell isn't still moving).
- Coach.0 alone is a small refactor; the Coach feature itself is a full batch. Easier to scope as one batch in Phase 4 than to scatter pieces across Phase 3.

**Workaround until shipped:** Open a spreadsheet, project monthly cash flow by hand, decide. (This is precisely the workflow Project Ceres exists to replace, which is what makes the Decision Coach a high-value Phase 4 candidate.)

**Gate:** Phase 4. Coach.0 lands as the first sub-stage of the Coach batch. Open the implementation plan via `superpowers:writing-plans` once the user schedules the work.

**Triggered by:** User brainstorm 2026-05-16 after a dentist's offer with two financing options exposed the gap in the app's pre-commit decision support.

---

### Import sandbox + admin tooling — SHELVED (was roadmap Stage 11.5)

**Status:** On hold, shelved 2026-06-29 ([ADR-0078](decisions/ADR-0078-import-shelved-from-phase-3-beta.md)). This is tooling *for* the import module, which is shelved from the Phase 3 beta (roadmap Stage 11.9). It does not execute in Phase 3 and resumes only if/when import is un-shelved. Relocated here from `roadmap-phase-three.md` on 2026-06-29 so the live Phase 3 roadmap carries no unchecked boxes under a shelved stage; the design is preserved verbatim below. Architecture pattern + sequencing rationale: [ADR-0072](decisions/ADR-0072-import-sandbox-as-separate-environment.md) (multi-environment separation, NOT runtime switch; lands post-SPA-migration).

**Goal:** the developer can iterate on parser bugs against fake bank data with zero risk of contaminating real data, including per-import bulk wipe and row-level multi-select delete. Same code, separate database, separate launch profile, separate port. The boundary is physical — different process, different database, different URL.

**Gate:** resumes only when import is un-shelved (reverses ADR-0078). At that point, restore a stage in the then-active roadmap from the sub-stages below.

#### Sub-stages

| # | Sub-stage | Spec / Reference |
|---|---|---|
| 11.5.1 | New `ASPNETCORE_ENVIRONMENT=Sandbox` + `appsettings.Sandbox.json` + `Sandbox` launch profile in `Properties/launchSettings.json` | ADR-0072 § Decision 1 |
| 11.5.2 | `project_ceres_sandbox` database created locally via `createdb`; `scripts/migrate-all.sh` applies migrations to both DBs | ADR-0072 § Decision 1 |
| 11.5.3 | Fail-fast startup check: refuses to boot the `Sandbox` build if connection string `Database=` does not end with `_sandbox` | ADR-0072 § Decision 1 |
| 11.5.4 | `ImportBatch` entity + nullable `ImportBatchId` FK on `Transaction`, `Transfer`, `LiabilityPayment`; `ImportService` mints a batch row per import and stamps every produced row | ADR-0072 § Decision 2 |
| 11.5.5 | Admin SPA route `/admin/import-batches` (list view + detail view) in `ProjectCeres.Client/`; uses shadcn DataTable + TanStack row selection; bulk-wipe and multi-select-delete actions | ADR-0072 § Decision 2 |
| 11.5.6 | Admin API endpoints register conditionally (`Sandbox` + `Development` only); architecture test asserts production 404 | ADR-0072 § Decision 1 |
| 11.5.7 | `IHostedService` seed runner gated to `Sandbox` environment: creates one sandbox user + fixed test accounts (Test Checking EUR / Test Checking USD / Test Savings / Test Credit Card) + default category set on first boot; idempotent | ADR-0072 § Decision 1 |

#### Verification checklist

Sandbox environment + database:

- [ ] `dotnet run --launch-profile Sandbox` boots the app against `project_ceres_sandbox` on a port distinct from `Development`
- [ ] `Development` and `Sandbox` builds can run simultaneously without port conflicts
- [ ] `scripts/migrate-all.sh` applies pending migrations to both databases and exits non-zero on any per-database failure
- [ ] Startup fail-fast: launching `Sandbox` with a connection string whose `Database=` value does not match `*_sandbox` throws before `app.Run()` (integration test against deliberate misconfiguration)
- [ ] Seed runner creates the fixed sandbox user + accounts + categories on first sandbox boot; idempotent on subsequent boots
- [ ] Seed runner is NOT registered when `EnvironmentName != "Sandbox"` (architecture test)

`ImportBatch` primitive:

- [ ] `ImportBatch` entity exists with `Id`, `UserId`, `StartedAt`, `SourceFileName`, `ParserVersion?`, `RowsTotal`, `RowsImported`, `Status` columns
- [ ] `Transaction`, `Transfer`, `LiabilityPayment` each carry nullable `ImportBatchId Guid?` FK with index
- [ ] EF query filter on `ImportBatch` registered alongside the other `IUserOwned` filters in `OnModelCreating`
- [ ] `ImportService.ImportAsync` creates the `ImportBatch` row first, sets its `Status = InProgress`, then stamps every produced row with `ImportBatchId`; final status set to `Succeeded` / `Failed` / `Aborted` before commit
- [ ] Existing rows from before Stage 11.5 have `ImportBatchId = null` and continue to read/write normally (back-compat test)
- [ ] Architecture test: every entity created by an `IImporter<T>` implementation has an `ImportBatchId` column

Admin SPA route:

- [ ] `/admin/import-batches` lists every batch in reverse-chronological order for the current user
- [ ] Per-batch "Delete batch" cascades to every produced row + the `ImportBatch` row itself, in a single transaction
- [ ] Detail view `/admin/import-batches/:id` lists the rows the batch produced with a checkbox column
- [ ] "Delete selected" deletes exactly the multi-selected rows in a single transaction; the `ImportBatch` row remains (only its `RowsImported` count is recalculated)
- [ ] Empty-state copy when a batch has no surviving rows (all were deleted individually)
- [ ] Admin route + API endpoints register only when `EnvironmentName` is `Sandbox` or `Development`
- [ ] Integration test: in `Production` environment, `GET /api/admin/import-batches` returns 404 (route does not exist in the route table)

Operational:

- [ ] README's "Setup" section documents creating the sandbox database + running `migrate-all.sh`
- [ ] `appsettings.Sandbox.json` is checked in; secrets (if any) live in User Secrets keyed to the Sandbox environment
- [ ] CI smoke test: spin up a throwaway Postgres, run `migrate-all.sh`, run the seed runner, import a checked-in fake CSV, assert the rows landed, bulk-wipe, assert the rows are gone

---

### Deferred from Stage 12 (2026-06-30)

Three items were scoped out of Stage 12 core during the 2026-06-30 brainstorm (user-authorized). Each needs design work the core stage deliberately did not carry. The `[ ]` execution checklist lives in `roadmap-phase-three.md` § Stage 12.5 (the trackable receiving stage); the WHAT + open design questions are preserved here. Source spec: `docs/superpowers/specs/2026-06-30-stage-12-sessions-support-spa-design.md` § Deferrals.

#### Per-session IP-anchor toggle (was roadmap Stage 12 / Stage 6 carry-forward)

**What:** a per-row "anchor this session to its creation IP" toggle on `/settings/sessions`; when enabled, requests carrying that session's cookie from a different IP are rejected.

**Why deferred:** the roadmap's Stage 6 carry-forward assumed `UserSession` already has the field and that this stage merely "exposes the toggle." It does **not** — `UserSession` fields are `Id, UserId, PersistentTokenHash, IpCreatedAt, UserAgent, CreatedAt, LastUsedAt, RevokedAt, IsPersistent, UsedBackupCodeAtLogin` (no `IsIpAnchored`). This is a new column + new server enforcement, not a UI exposure — bigger and riskier than written.

**Open design questions to resolve before building:**
- Anchoring granularity: exact-IP (self-locks-out roaming mobile/CGNAT/VPN users) vs subnet vs ASN. Exact-IP is the naive choice and the dangerous one.
- Where enforcement hooks: alongside `UserBlockedIpMiddleware`, or in `SessionRevocationValidator`?
- Self-lockout guard: a user on mobile data who anchors will lose the session on the next tower hop. Need a UX warning + a recovery path (it shouldn't require account recovery to undo).
- Migration: adds a column to an auth-internal RLS table — confirm the RLS migration + parity implications.

#### Admin ticket-list UI (was part of roadmap Stage 12.6)

**What:** an admin surface to list/triage all users' support tickets (Stage 12 core ships admin-NOTIFY email only — `EmailTemplateKey.SupportTicketReceived` to the configured admin address).

**Why deferred:** there is no roles / admin-identity system in the codebase today — no `api/admin`, no role claims, no admin-user concept. ADR-0065 names an `Admin/` endpoint scope but nothing implements it. Building the list UI first requires deciding and building the admin-identity mechanism.

**Open design questions:**
- How is "admin" identified: a role claim on the auth cookie? A configured admin email allow-list? A single-operator assumption for the beta?
- Endpoint scope + isolation: admin endpoints read across users (bypass the per-user query filter / RLS) — they must use the `ceres_admin` BYPASSRLS context deliberately and be access-controlled, mirroring the `IUserJobRunner` admin-context discipline.
- Whether the beta needs this at all, or admin triage via the notification email + direct DB access suffices until there's a second operator.

#### New-session-from-new-IP alert email — ✅ SHIPPED 2026-09-08 (Stage 12.5.3)

**What shipped:** when a `UserSession` is created from an IP the user has never signed in from before, an email alert is sent ("new sign-in from a network we haven't seen; if it wasn't you, change your password and review your sessions"), carrying the IP, device summary, and sign-in time.

**Design resolved:**
- Comparison granularity: **exact IP** (fork 1a — mirrors the §12.5.1 IP-anchor decision; subnet/ASN rejected for the same reasons).
- First-ever-login suppression: **yes** — no prior session means no "new" to alert on; every first sign-in would otherwise fire it.
- Template: `NewSessionAlert` added to `EmailTemplateKey` + EN/ES resx. Sent by a non-blocking `INewSessionNotificationService` (log-and-swallow — a mail outage never fails the login).

**Still deferred — the opt-out toggle (→ roadmap Stage 17 — Notification preferences):** the alert ships WITHOUT an off-switch, because (a) the notification-preferences surface it would live in does not exist yet and (b) a new-sign-in security alert is conventionally not opt-out (Google/GitHub don't offer one). The toggle is homed as a `[ ]` under roadmap **Stage 17 — Notification preferences**, alongside the weekly-digest and Safe-to-Spend toggles that also wait on that surface. (Was briefly tracked as "§12.5.5"; renumbered out of the 12-family on 2026-09-10 since a 12-numbered stage is 12-family.)

**Note vs. the original spec:** the shipped email does NOT include a one-click revoke *link* (the original "wasn't you? revoke it" idea) — it points the user to Settings → Security to review/revoke sessions instead. A signed revoke-link is a future enhancement, not owed now.

**Cross-cutting follow-up — detach notification sends at scale (not scheduled):** every notification email in the app (`NewSessionNotificationService`, `SupportNotificationService`, `PasswordResetService` completion mail) is `await`ed inline in its request path and swallows failures. A send *failure* never fails the operation (the guarantee holds), but a *slow* provider adds its latency to the response up to the HttpClient timeout. This is consistent across all notification call sites by design, not a §12.5.3 regression. If login/request latency becomes a budget concern, the fix is one place: a shared detached-send wrapper (`Task.Run` with an observed-exception logger + a send timeout), applied to all notification sites at once — not a per-site divergence. Raised by the §12.5.3 security review (non-blocking note).

---

## Phase 5 — Business Model

**Gate: Phase 4 must be stable. See [`business-model.md`](business-model.md) for full detail.**

Goal: make the product self-sustaining via a freemium tier structure.

- **Free tier:** core personal finance tracking (manual entry, file import, reports)
- **Premium tier:** bank connectivity and autónomo features
- Authentication and multi-tenancy from Phase 3 are prerequisites for any billing system
- **Passkeys / WebAuthn** — optional alternative to password + MFA for maximum security. Private key stays on the user's device; server holds only the public key. Adds significant implementation complexity — only appropriate once the Phase 3 auth foundation is stable.
- **Paid restoration service** — when a user who churned naturally (no erasure request) returns after their grace period but before permanent deletion, restoring their archived data is a paid service. Fee set at archive creation time. Users who submitted a GDPR erasure request cannot use this service. See ADR-0029 for the full lifecycle and `CustomerArchive` schema. Do not build before Phase 5.
