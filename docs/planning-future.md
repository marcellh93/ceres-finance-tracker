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

### Projection and Insights Engine

A forward-looking analysis layer that generates personalised spending projections and actionable recommendations based on historical patterns. Seeded by the Phase 3 Safe to Spend alert (which is the MVP safety net for when spending headroom hits zero).

**Core capabilities:**
- **Spending projection:** given current-period spending velocity and days remaining, project whether the user will overshoot their budgets by end of period. "At your current pace, you will exceed your Food budget by €120 this month."
- **Safe to Spend runway:** project how many days the current Safe to Spend value will last at the user's average daily spending rate.
- **Anomaly detection:** flag transactions that are significantly above the user's typical amount for that category ("This €340 electricity bill is 2.4× your usual amount — is this correct?").
- **Seasonal pattern recognition:** recognise recurring cost spikes (annual insurance renewals, quarterly IVA payments, holiday spending) and warn the user ahead of time.
- **Personalised recommendations:** surface one actionable insight per week on the dashboard, derived from actual patterns in the user's data (e.g. "Your transport costs have increased 40% over the last 3 months — the largest driver is fuel").

**Implementation approach (Phase 4+):**
- All projections are derived from existing transaction data — no external data sources required.
- Insights are generated by a background job (shares infrastructure with the `FinancialNotificationJob` from Phase 3) and stored in an `Insight` table with a `GeneratedAt` timestamp and `DismissedAt` nullable column.
- The dashboard displays the most recent undismissed insight. Users can dismiss insights individually.
- No ML required for Phase 4 — rule-based heuristics over the transaction history are sufficient for all listed capabilities.

Gate: requires at least 3 months of transaction history per user to produce meaningful projections. Do not surface projection features to new users — show an onboarding prompt instead ("Add 3 months of history to unlock spending projections").

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

---

## Phase 5 — Business Model

**Gate: Phase 4 must be stable. See [`business-model.md`](business-model.md) for full detail.**

Goal: make the product self-sustaining via a freemium tier structure.

- **Free tier:** core personal finance tracking (manual entry, file import, reports)
- **Premium tier:** bank connectivity and autónomo features
- Authentication and multi-tenancy from Phase 3 are prerequisites for any billing system
- **Passkeys / WebAuthn** — optional alternative to password + MFA for maximum security. Private key stays on the user's device; server holds only the public key. Adds significant implementation complexity — only appropriate once the Phase 3 auth foundation is stable.
- **Paid restoration service** — when a user who churned naturally (no erasure request) returns after their grace period but before permanent deletion, restoring their archived data is a paid service. Fee set at archive creation time. Users who submitted a GDPR erasure request cannot use this service. See ADR-0029 for the full lifecycle and `CustomerArchive` schema. Do not build before Phase 5.
