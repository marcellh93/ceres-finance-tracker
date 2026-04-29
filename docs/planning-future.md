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

**Guiding product principle (from `docs/gaps-review.md`):**
> At every step, ask: "Does this help the user decide something?" If not, it is likely not part of the core product differentiation.

Value is not in features. Value is in making improvement visible and measurable.

---

### Strategic Positioning Note

> See `docs/gaps-review.md` for the full product gap analysis that informs Phase 4+ direction.

The competitive angle for Project Ceres is **low-friction financial visibility** — not bank sync speed, not feature count. The target evolution is from a financial tracking system to a **financial decision-making and life-planning platform**.

The app should not compete on bank connectivity (expensive, unreliable, PSD2-heavy). It should compete on:
- The quality of insight derived from the data the user already has
- The reduction of cognitive load ("How much can I spend?" answered immediately)
- Making improvement visible so users feel the value of continued use

---

## Maybe / Future Consideration

These ideas have merit but are not assigned to a phase yet. Revisit when the app is in daily use.

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

## Phase 5 — Business Model

**Gate: Phase 4 must be stable. See [`business-model.md`](business-model.md) for full detail.**

Goal: make the product self-sustaining via a freemium tier structure.

- **Free tier:** core personal finance tracking (manual entry, file import, reports)
- **Premium tier:** bank connectivity and autónomo features
- Authentication and multi-tenancy from Phase 3 are prerequisites for any billing system
- **Passkeys / WebAuthn** — optional alternative to password + MFA for maximum security. Private key stays on the user's device; server holds only the public key. Adds significant implementation complexity — only appropriate once the Phase 3 auth foundation is stable.
- **Paid restoration service** — when a user who churned naturally (no erasure request) returns after their grace period but before permanent deletion, restoring their archived data is a paid service. Fee set at archive creation time. Users who submitted a GDPR erasure request cannot use this service. See ADR-0029 for the full lifecycle and `CustomerArchive` schema. Do not build before Phase 5.
