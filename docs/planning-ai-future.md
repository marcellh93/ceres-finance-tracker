# Planning — AI Feature Proposals (Future)

This is a backlog of AI feature ideas for Project Ceres. Nothing here is committed; it's a menu to choose from once the team is ready to add AI to the product. Each idea is written so it could be lifted out, scoped, and turned into a `docs/superpowers/specs/...` design without further research.

> **Status:** brainstorm complete, no design or spec yet. The full conversation that produced this list lives in chat; this file is the durable record.

## Index

- [Mission framing — *track, defend, triage*](#mission-framing--track-defend-triage)
- [Guiding constraints (apply to every idea)](#guiding-constraints-apply-to-every-idea)
- [A. Capture — lower the friction of getting data in](#a-capture--lower-the-friction-of-getting-data-in)
  - [A1. Receipt-to-Transaction (OCR + LLM)](#a1-receipt-to-transaction-ocr--llm)
  - [A2. Statement-PDF / screenshot importer](#a2-statement-pdf--screenshot-importer)
  - [A3. Natural-language quick-add](#a3-natural-language-quick-add)
  - [A4. Line-item extraction from receipts (extension of A1)](#a4-line-item-extraction-from-receipts-extension-of-a1)
- [B. Categorization & review](#b-categorization--review)
  - [B1. Smart categorization on import](#b1-smart-categorization-on-import)
  - [B2. Duplicate / near-duplicate detector](#b2-duplicate--near-duplicate-detector)
- [C. Insight — turn data into understanding](#c-insight--turn-data-into-understanding)
  - [C1. Financial Q&A assistant ("Ask Ceres")](#c1-financial-qa-assistant-ask-ceres)
  - [C2. Subscription / recurring-charge detector](#c2-subscription--recurring-charge-detector)
  - [C3. Cashflow forecast](#c3-cashflow-forecast)
  - [C4. Anomaly watch](#c4-anomaly-watch)
  - [C5. Monthly narrative ("Why was this month different?")](#c5-monthly-narrative-why-was-this-month-different)
- [D. Goals & behavior](#d-goals--behavior)
  - [D1. Goal coach](#d1-goal-coach)
  - [D2. Budget recommender](#d2-budget-recommender)
- [E. Spain — autónomo-specific](#e-spain--autónomo-specific)
  - [E1. Tax-deductibility classifier](#e1-tax-deductibility-classifier)
  - [E2. Mixed-use expense splitter](#e2-mixed-use-expense-splitter)
- [F. USA — 1099 / Schedule C-specific](#f-usa--1099--schedule-c-specific)
  - [F1. Schedule C category mapper](#f1-schedule-c-category-mapper)
  - [F2. Tax setaside auto-suggester](#f2-tax-setaside-auto-suggester)
  - [F3. Quarterly estimated-tax forecaster](#f3-quarterly-estimated-tax-forecaster)
  - [F4. Mileage auto-logger and retrospective reconstruction](#f4-mileage-auto-logger-and-retrospective-reconstruction)
- [G. Affordability-first / triage features](#g-affordability-first--triage-features)
  - [G1. Shrinkflation / unit-price tracker](#g1-shrinkflation--unit-price-tracker)
  - [G2. Price-creep watch on recurring charges](#g2-price-creep-watch-on-recurring-charges)
  - [G3. Private-label / cheaper-equivalent suggester](#g3-private-label--cheaper-equivalent-suggester)
  - [G4. No-spend mode / envelope lock](#g4-no-spend-mode--envelope-lock)
  - [G5. €500 / $500 emergency-buffer milestone](#g5-500--500-emergency-buffer-milestone)
  - [G6. Triage dashboard variant](#g6-triage-dashboard-variant)
  - [G7. Bills-due-this-week early warning](#g7-bills-due-this-week-early-warning)
  - [G8. Inflation-adjusted view of personal categories](#g8-inflation-adjusted-view-of-personal-categories)
- [Recommended bundles](#recommended-bundles)
- [Out of scope](#out-of-scope)
- [Sources](#sources)

## Mission framing — *track, defend, triage*

Project Ceres started as a tracker that replaces spreadsheets. The macro context the product now ships into has shifted:

- **Affordability-first as a consumer identity.** EY's Future Consumer Index reports the "Affordability First" segment grew from 26% to 38% of US consumers — more than 1 in 3 people now lead with cost on every purchase decision, and openly identify with frugality rather than hiding it.
- **Acute distress is broad, not niche.** 39% of US households reported income decline in 2025 (nearly double the prior year); 80% cite inflation as their top concern; 54% of European consumers are pessimistic about their economic future (73% France, 71% Romania); 50% of consumers worry often or constantly about putting food on the table; 44% of US women cannot cover a $500 medical emergency (up 37% in two years); Gen Z holds under one month of savings.
- **Behavior has shifted to deal-seeking.** 76% of UK purchase decisions are now discount-driven; 64% of shoppers visit multiple physical stores comparing prices (up from 56%); 65% buy private-label regularly; 73% of millennials specifically asked for shrinkflation alerts. Food prices in the US are 31% higher than 2019 vs. 26% general CPI growth — food inflation outpaces general inflation, and food is non-optional spend.
- **Frugality has lost its stigma.** "No Buy" minimalist communities are visible and growing on social media. Users will openly adopt and discuss tools that help them spend less.

This expands what Ceres should *do*, not what it *is*. The product remains a single-entry personal-finance tracker — but tracking alone is insufficient for this audience. Two additional modes need first-class support:

1. **Defend** the user against silent value extraction — shrinkflation, price-creep on recurring charges, forgotten subscriptions, vendor overcharges, and the gap between what they used to pay and what they pay now.
2. **Triage** the user's next 30 days when buffers are thin — runway, bill-coverage alarms, the single most overspent category this period, and one clear next action.

Every idea below is evaluated against this lens. Features that only *track* are still useful, but features that *defend* or *triage* are now the priority direction for the product.

## Guiding constraints (apply to every idea)

- **Single-entry bookkeeping** — no double-entry, no debits/credits.
- **No bank feeds** — data arrives via manual entry, quick-add, or CSV/OFX import.
- **Verifiable output** — when AI produces a value (category, amount, deduction), the user must see what it produced, *why*, and be able to override before it's persisted.
- **Audience split** — Project Ceres serves two personas: an individual tracking household finances, and an autónomo / 1099 freelancer who also needs help with tax-related context. Region-specific features live in the Spain and USA sections; everything else applies to both.
- **No fiduciary advice** — the assistant explains and suggests; it never asserts that something *is* deductible or *will* happen. Language must be advisory.

---

## A. Capture — lower the friction of getting data in

### A1. Receipt-to-Transaction (OCR + LLM)
Snap or upload a receipt. The model extracts vendor, date, amount, currency, and proposes category + account. Saves the receipt as a `TransactionAttachment`. The image is shown next to the extracted fields so AI errors are cheap.

- **Why valuable:** removes the most tedious workflow in the app.
- **Existing schema fit:** `Transaction` + `TransactionAttachment` already exist; `StoredPath` / `FileName` separation is already correct for this.
- **Bounded scope:** one screen, one model call, one record.
- **Risks:** OCR quality on crumpled / non-Latin receipts; multi-currency receipts; line-item-level extraction (see A4).

### A2. Statement-PDF / screenshot importer
User drops a bank statement PDF or a screenshot of a banking app, AI converts it into staged rows for the existing import-review flow.

- **Why valuable:** replaces the single biggest pain of having no bank feeds — copying transactions out of a PDF.
- **Existing schema fit:** uses `ImportStagedTransaction` and the existing review UX.
- **Risks:** statement layouts vary wildly per bank; balance-row vs. transaction-row disambiguation; date format inference.

### A3. Natural-language quick-add
Type or dictate `"€45 groceries at Mercadona yesterday from BBVA"` → fills the quick-add form (amount, category, account, date, description).

- **Why valuable:** mobile-first input. Pairs well with the Phase 3 responsive work already in flight.
- **Existing schema fit:** drives the existing `Create*Request` DTOs.
- **Risks:** parsing ambiguous account/category names; multi-language input; voice transcription quality.

### A4. Line-item extraction from receipts (extension of A1)
Beyond the receipt total, split a single Mercadona receipt into food vs. household items, generating multiple transactions or a sub-categorized one.

- **Why valuable:** mixed receipts are common (groceries + cleaning supplies + a deductible item).
- **Risks:** receipts often don't have machine-readable item categories — model has to infer; a single transaction split across categories doesn't fit the current model and would require a sibling-record or split-record extension.

---

## B. Categorization & review

### B1. Smart categorization on import
Replace or augment the user-written rule system. The LLM few-shots from the user's own prior `Transaction` history, returns a confidence score per row, and offers to *promote* a confident pattern into a persistent `CsvImportProfile` rule.

- **Why valuable:** today the user writes rules by hand; this turns rule-writing into rule-confirming.
- **Existing schema fit:** plugs into the staged-import review UI.
- **Risks:** prompt size grows with history — needs a sampling/embedding strategy; cold start (new users have no history).

### B2. Duplicate / near-duplicate detector
Already partially planned in `docs/planning-future.md` via content fingerprints. AI extension catches fuzzy duplicates the hash misses (different descriptions for the same charge, off-by-a-day clearing dates).

- **Why valuable:** prevents the most common import mistake.
- **Risks:** false positives are annoying — must default to "flag, don't suppress."

---

## C. Insight — turn data into understanding

### C1. Financial Q&A assistant ("Ask Ceres")
Natural-language chat over the user's own data via a tool-use loop. The LLM calls read-only query functions against existing services — *no SQL generation*. Example questions: "How much did I spend on client lunches last quarter?", "Am I on track to hit my emergency fund goal?", "What's the biggest single category change vs. last month?"

- **Why valuable:** high wow-factor; surfaces existing data without forcing the user to learn the reports UI.
- **Existing schema fit:** read paths already exist via Movements/Transactions/Reports services.
- **Risks:** hallucination if the tool layer is too thin; needs aggressive constraint that the model only uses returned tool data, never its own knowledge of finances.

### C2. Subscription / recurring-charge detector
Scan transaction history, surface recurring patterns the user hasn't yet modeled as `RecurringTransaction`, flag suspected forgotten subs, and offer "add as recurring" with a single click.

- **Why valuable:** Rocket Money and Origin built whole products around this — users love seeing forgotten subscriptions surface.
- **Existing schema fit:** `RecurringTransaction` already exists and would be the target.
- **Risks:** distinguishing recurring from coincidental (two coffees on the 1st); price-creep detection on subscriptions is a natural follow-on.

### C3. Cashflow forecast
Predict end-of-month / 30-day balance per account using `RecurringTransaction`, scheduled `LiabilityPayment`s, and historical seasonality.

- **Why valuable:** maps directly to the "Safe to Spend" idea already sketched in `planning-future.md`.
- **Existing schema fit:** already has the inputs (recurring + scheduled + history).
- **Risks:** confidence intervals matter — a single number is misleading; need to express uncertainty.

### C4. Anomaly watch
Flag transactions notably above the user's own baseline for a vendor or category ("This €340 electricity bill is 2.4× your usual").

- **Why valuable:** already named in `planning-future.md` as a future capability.
- **Existing schema fit:** purely reads `Transaction` + `Category`.
- **Risks:** baselining for low-frequency vendors; suppressing repeat alerts.

### C5. Monthly narrative ("Why was this month different?")
Diff this month vs. prior month / 3-month avg and produce a 3-bullet written explanation with the biggest movers.

- **Why valuable:** strong email-digest material; pairs with the weekly digest job already planned for Phase 3.
- **Risks:** narrative quality — must avoid sounding like a horoscope. Anchor every bullet to a specific transaction or aggregate.

---

## D. Goals & behavior

### D1. Goal coach
Given a target ("$10k emergency fund by Dec 2027" / "€50k net worth by 2027"), AI proposes a monthly contribution plan based on the user's actual savings rate, and re-plans when they fall behind.

- **Why valuable:** today `Budget` (goal type) is set-and-forget; this makes it adaptive.
- **Existing schema fit:** builds on existing `Budget` (goal) and the proposed `NetWorthMilestone`.
- **Risks:** "you'll never make it at this rate" is a sensitive message — UX matters more than model.

### D2. Budget recommender
When creating a new `CategoryBudget`, AI suggests a starting amount based on trailing 3–6 months of spending, with a one-line explanation.

- **Why valuable:** small but constantly-useful nudge — most people don't know what a realistic budget is.
- **Existing schema fit:** drives `CategoryBudget` creation form only.
- **Risks:** very low.

---

## E. Spain — autónomo-specific

> **Persona:** sole trader (autónomo) tracking mixed personal + business activity in the same accounts.

### E1. Tax-deductibility classifier
For each Expense `Transaction`, AI proposes deductible-yes/no plus a business-use percentage, with a one-sentence reasoning the user can accept or reject. Stored as a per-transaction annotation.

- **Why valuable:** the moat for the autónomo audience. Xolo/Renn/Companio automate workflows but don't do per-transaction AI deductibility.
- **Existing schema fit:** would add a per-transaction annotation entity; the underlying `Transaction` stays unchanged.
- **Risks:** the user is responsible for what they declare to Hacienda — copy must be advisory ("looks deductible based on similar past transactions").

### E2. Mixed-use expense splitter
For categories likely to be mixed (phone, internet, vehicle, home-office, electricity), AI suggests an allocation % per category based on user-stated work pattern, generating a draft split for each transaction in those categories.

- **Why valuable:** directly answers an open question in `planning-future.md` about mixed personal/business categorization for autónomos.
- **Existing schema fit:** depends on whether splits are stored as separate transactions or as an annotation; same modeling decision as E1.
- **Risks:** same as E1 — advisory only.

---

## F. USA — 1099 / Schedule C-specific

> **Persona:** independent contractor or sole proprietor filing Schedule C, paying quarterly estimated taxes. Handles both household and business activity.

### F1. Schedule C category mapper
For each Expense `Transaction`, AI proposes the Schedule C line that matches (Line 8 Advertising, Line 9 Car/Truck, Line 17 Legal/Professional, Line 18 Office, Line 24a Travel, Line 24b Meals, etc.). Stored as a per-transaction annotation alongside the user's own category.

- **Why valuable:** miscategorization is the single most common tax mistake for small businesses; this turns the existing free-form `Category` into something the user (or their accountant) can hand to a preparer.
- **Existing schema fit:** annotation only; doesn't change `Category` or `Transaction`.
- **Risks:** Schedule C lines change rarely but do change — the line dictionary needs to be data, not hardcoded.

### F2. Tax setaside auto-suggester
For every Income `Transaction` from a designated 1099 / business account, AI suggests a transfer to a "tax bucket" account at a percentage tuned to the user's effective rate (typically 25–30%). The transfer is a regular `Transfer` between two accounts the user already owns — the AI doesn't move money, only proposes the entry.

- **Why valuable:** Lili built a whole product feature around this. Autónomos in Spain have an analogous need (set aside for trimestral IRPF/IVA payments).
- **Existing schema fit:** uses existing `Transfer`. The "tax bucket" is just an `Account`. The percentage learns from prior end-of-year settlements.
- **Risks:** the user must confirm each transfer or opt into auto-confirm with a clear ceiling; cross-currency setaside is out of scope (transfers must share currency).

### F3. Quarterly estimated-tax forecaster
Project Q1/Q2/Q3/Q4 estimated tax payments based on year-to-date income and expenses. Send a reminder N days before each IRS deadline (Apr 15, Jun 16, Sep 15, Jan 15) with the projected payment amount and a breakdown.

- **Why valuable:** the IRS does not send reminders. Underpayment penalties are a recurring source of pain for new freelancers.
- **Existing schema fit:** runs on the same `FinancialNotificationJob` already planned for Phase 3 weekly digest.
- **Risks:** projections must be marked as estimates; safe-harbor logic (110% of prior year) is non-trivial.

### F4. Mileage auto-logger and retrospective reconstruction
*Mobile-only.* Detect drives via phone GPS, classify as business/personal with a swipe, persist as a `Transaction` against a `Mileage` category at the IRS standard rate ($0.725/mi for 2026). Retrospective: given calendar entries and known client locations, AI reconstructs an IRS-compliant mileage log for past months.

- **Why valuable:** Hurdlr, Everlance, MileIQ, and MileageWise all built businesses on this. Worth ~$7,250 per 10,000 mi.
- **Existing schema fit:** mileage as a `Transaction` against a special category; IRS rate stored alongside currency reference data.
- **Risks:** requires native-mobile or PWA-with-background-geolocation, neither of which exists yet — this gates on the future native apps mentioned in `planning-future.md`.

---

## G. Affordability-first / triage features

> **Lens:** *defend* against silent value extraction; *triage* the next 30 days. Driven by the macro shift described in the Mission framing section above.

### G1. Shrinkflation / unit-price tracker
Receipt OCR (A1) extracts line items with quantity (e.g. *Hellmann's Mayo 400g €3.20*). The app maintains a per-product price-per-unit history keyed by a normalized product identity. The monthly digest flags movement: *"Hellmann's mayo: €/100g up 14% since January; package shrunk 450g → 400g."*

- **Why valuable:** 73% of millennials explicitly asked for this. No mainstream tracker does it well.
- **AI value:** entity resolution across receipt variants (*"Hellmann's Mayo Real 400g"* = *"Mayonesa Hellmann's"*); extraction of pack size from inconsistent receipt text.
- **Existing schema fit:** requires a new `Product` (or normalized-line-item) entity tied to `TransactionAttachment` extractions. `Transaction` itself stays unchanged.
- **Risks:** entity resolution failures double-count or miss items; receipts that omit pack size (common in restaurants, services) are not eligible.

### G2. Price-creep watch on recurring charges
Subset of C4 anomaly watch but specific to `RecurringTransaction`. Surfaces *trend* increases on subscriptions, rent, gym, insurance, utilities. *"Netflix went €11.99 → €13.99 in March; that's €24/year more at this rate."*

- **Why valuable:** the brief says merchants are quietly charging more; the user's own subscriptions are the easiest place to defend.
- **AI value:** small — mostly arithmetic and grouping. AI helps identify same-merchant variants when the descriptor changes.
- **Existing schema fit:** reads `RecurringTransaction` and linked `Transaction`s only.
- **Risks:** noisy when subscriptions are paused/resumed; needs a clear "ignore" affordance.

### G3. Private-label / cheaper-equivalent suggester
Receipt OCR sees *"Coca-Cola 2L €2.40 at Mercadona."* A category-aware suggester returns: *"Hacendado cola 2L is typically €0.90 here — switching saves ~€78/year at your purchase rate."*

- **Why valuable:** 65% buy private-label regularly. This makes the affordability-first identity actionable inside the app.
- **AI value:** mapping branded items → store-specific private-label equivalents; estimating annualized savings from the user's purchase frequency.
- **Existing schema fit:** depends on G1's product identity work; otherwise additive.
- **Risks:** the brand→private-label mapping is real, bounded reference data but it must be maintained per-region (Mercadona/Hacendado in Spain, Kirkland/Costco in US, etc.). Suggestions must never be moralizing.

### G4. No-spend mode / envelope lock
Opt-in toggle: *"I am in No Buy until 30 May."* While active, any transaction in user-tagged discretionary categories triggers an immediate notification and a running tally. AI's role is to *recommend* which categories to include based on the user's prior discretionary spend; the lock itself is pure feature work.

- **Why valuable:** "No Buy" is a visible cultural movement. Adoption requires the app to support the identity rather than judge it.
- **Existing schema fit:** new `SpendingMode` (or extended `Settings` row) with active dates and a category allowlist; no change to `Transaction`.
- **Risks:** must be opt-in; copy must read as supportive ("you broke No Buy with €4.20 at Starbucks — running tally €11.40") not punitive.

### G5. €500 / $500 emergency-buffer milestone
A specific named goal type: build a $500/€500 buffer in a designated `Account`. First-class onboarding flow, dedicated progress nudge in the digest, opt-in auto-setaside (reuses F2 mechanics) targeting the buffer until the threshold is reached.

- **Why valuable:** 44% of US women cannot cover a $500 medical emergency. A precisely-named, clearly-bounded goal converts an abstract financial fear into a concrete product action.
- **Existing schema fit:** specialization of the proposed `NetWorthMilestone` or a sibling `EmergencyBufferMilestone`; reuses `Transfer` for the setaside mechanic.
- **Risks:** the threshold value is region-dependent; must be configurable. Avoid implying medical-emergency advice.

### G6. Triage dashboard variant
Today's dashboard answers *"how am I doing?"* The triage dashboard answers *"what do I do right now?"* — surfaces, in fixed order: days of runway at current spend rate, the next bill that will fail to clear if nothing changes, the single category most over budget this month, and one recommended action.

- **Why valuable:** under acute financial stress, users do not browse — they need an answer surfaced first. This is an information-architecture win, not a model win.
- **AI value:** small for the dashboard itself (rules + arithmetic); the "one recommended action" line benefits from LLM phrasing.
- **Existing schema fit:** reads everything that already exists; surfaces it differently. Likely a user preference toggle between "tracker" and "triage" modes.
- **Risks:** must coexist with the standard dashboard, not replace it; users in calmer states should not be forced into triage framing.

### G7. Bills-due-this-week early warning
Distinct from C3 forecast: a hard alarm when specific dated obligations (`LiabilityPayment`, scheduled `RecurringTransaction`) for the coming N days exceed the cleared balance of the relevant `Account`. *"Rent €850 hits in 6 days; Checking shows €620 cleared."*

- **Why valuable:** triage. Prevents the bill that pushes a thin month into overdraft.
- **AI value:** minimal — arithmetic and date math. AI helps phrase the suggested action ("move €230 from Savings, or skip groceries day on Tuesday").
- **Existing schema fit:** reads `LiabilityPayment`, `RecurringTransaction`, `Account` ledger; runs on the Phase 3 `FinancialNotificationJob`.
- **Risks:** must respect cleared-vs-pending distinction; must not double-count transfers already scheduled.

### G8. Inflation-adjusted view of personal categories
Show the user how *their* spend in a category has tracked against a reference index (general CPI, food CPI, energy CPI) over 12–24 months. Anchors the "is it me, or is it the world?" question many in this segment are silently asking.

- **Why valuable:** addresses the psychological dimension of the brief — users want to know whether their distress is personal failure or shared external pressure. Most of the time, the answer is the latter, and seeing that proven in their own data reduces shame.
- **AI value:** small for the chart itself. AI value lies in narrative phrasing in the digest ("Your grocery spend rose 22% over 24 months; food CPI rose 14%. Roughly two-thirds of your increase is general inflation; the rest is personal").
- **Existing schema fit:** reads `Transaction` + `Category`; needs a CPI reference series (public, free) cached locally.
- **Risks:** wrong index choice (e.g. comparing groceries to general CPI) is misleading; must pick a sensible default per category.

---

## Recommended bundles

If the team decides to ship a *cohesive* first AI release, two bundles dominate:

- **Mass-market bundle:** A1 Receipt-to-Transaction + B1 Smart import categorization + C2 Subscription detector. These three together transform the data-capture and review experience for every user, regardless of region. Lowest scope risk.
- **Autónomo bundle (Spain):** A1 + E1 Deductibility classifier + E2 Mixed-use splitter. Same LLM call can serve A1's extraction and E1's classification. The product story becomes "take a photo of a coffee, and Ceres knows it was 100% deductible because client meeting." That is a story Xolo/Renn/Companio do not currently tell.
- **Freelancer bundle (USA):** A1 + F1 Schedule C mapper + F2 Tax setaside + F3 Quarterly forecaster. Same composability as the Spain bundle, plus F2/F3 push the app from "tracker" toward "tax-aware tracker" without crossing into being tax-prep software.
- **Affordability-first bundle:** G6 Triage dashboard + G7 Bills-due alarm + C3 Cashflow forecast + G2 Price-creep watch + G5 €500 buffer milestone. This is the *defend + triage* bundle aimed at the affordability-first identity. It reframes the product from "see your money" to "survive this month and stop value being silently extracted from you." Strong fit for the macro context the product now ships into.

---

## Out of scope

Listed here so a future conversation does not relitigate.

- **Quarterly tax-prep helper for Modelo 130 / 303 (Spain).** Generating the actual tax filing is tax-preparation software, a distinct product category. Project Ceres is a personal-finance tracker.
- **Verifactu invoicing compliance (Spain).** Verifactu governs *issuing* invoices, not tracking personal finance. The deadline for autónomos was postponed to **1 July 2027**. If Project Ceres ever expands to issue invoices to clients, revisit then.
- **Schedule C tax filing (USA).** Same reasoning as the Modelo helper above. Mapping transactions to Schedule C lines (F1) is in scope; *filing* the return is not.
- **Currency conversion driven by AI insight.** Already excluded by the core architecture rules in `CLAUDE.md`.
- **Cross-currency transfers.** Same.

---

## Sources

### Macro / consumer-context research
- EY Future Consumer Index — "Affordability First" segment growth (26% → 38%, US)
- KPMG 2025 consumer pulse — 39% US household income decline; 80% inflation as top concern
- BCG European consumer sentiment — 54% pessimistic (73% France, 71% Romania)
- UK retail data — 76% of purchases discount-driven; 64% multi-store deal-hunting (up from 56%); 65% private-label
- Millennial survey — 73% want shrinkflation alerts
- US food CPI — 31% above 2019 vs. 26% general CPI
- "No Buy" minimalist movement — visible across social platforms

### General AI in personal finance
- [Best AI Personal Finance Tools in 2026 — Techno-Pulse](https://www.techno-pulse.com/2026/04/best-ai-personal-finance-tools-in-2026.html)
- [AI in Personal Finance 2026 — Origin](https://useorigin.com/resources/blog/ai-in-personal-finance-2026-comparing-the-top-tools-and-approaches)
- [Best AI Personal Finance Tools 2026 — Toolradar](https://toolradar.com/guides/best-ai-personal-finance-tools)
- [Best Receipt Scanner Apps in 2026 — Finny](https://getfinny.app/blog/best-receipt-scanner-apps-2026)
- [Top AI Tools for Subscription Cancellation 2026 — Fini Labs](https://www.usefini.com/guides/top-ai-tools-subscription-cancellation)
- [Rocket Money — Subscription Manager](https://www.rocketmoney.com/)
- [Top Financial Planning Software with Cash Flow Analysis — Quicken](https://www.quicken.com/blog/top-financial-planning-software-with-cash-flow-analysis-features/)
- [Tendi — AI Financial Advisor](https://tendi.ai/)
- [Cleo — AI Money Assistant](https://web.meetcleo.com/)
- [Conversational AI for Finance — Finextra](https://www.finextra.com/blogposting/30308/the-rise-of-conversational-payments-how-ai-is-turning-dialogue-into-a-financial-interface)

### Spain — autónomo
- [VeriFactu Entry Into Force: Spain 2026 Dates — Renn](https://getrenn.com/blog/verifactu-entry-into-force)
- [Spanish E-Invoicing 2026: VeriFactu Explained — Peppol](https://www.peppol.nu/blog-items/spaanse-e-invoicing-2026-verifactu/)
- [Self-Employed Accounting Software in Spain — Renn](https://getrenn.com/blog/self-employed-accounting-software)
- [Xolo — All-in-one for Spanish freelancers](https://www.xolo.io/es-en)

### USA — 1099 / freelancer / Schedule C
- [Best Personal Finance Apps for Freelancers 2026 — Origin](https://useorigin.com/resources/blog/best-personal-finance-apps-for-freelancers-in-2026)
- [Best AI Finance Tools for Self-Employed 2026 — Cashflowy](https://www.cashflowy.ai/blog/best-ai-finance-tools-for-self-employed)
- [Keeper — AI tax filing for 1099](https://www.keepertax.com/)
- [FlyFin — AI tax service for self-employed](https://flyfin.tax/)
- [Deduct AI — Apple App Store](https://apps.apple.com/us/app/deduct-ai-expense-tracker/id6744041920)
- [Hurdlr — Mileage and expense for freelancers](https://www.hurdlr.com/)
- [Everlance — Mileage tracker](https://www.everlance.com/)
- [MileIQ — Automatic mileage tracker](https://mileiq.com/)
- [Lili — Tax bucket for freelancers](https://support.lili.co/hc/en-us/articles/360039640591-How-do-I-open-a-tax-bucket-and-set-up-automatic-tax-savings)
- [Schedule C Expense Categories Guide 2026 — ReceiptSync](https://receiptsync.net/blog/schedule-c-expense-categories-complete-guide)
- [IRS — Estimated taxes](https://www.irs.gov/businesses/small-businesses-self-employed/estimated-taxes)
- [Quarterly Tax Deadlines 2026 — Wealthvieu](https://wealthvieu.com/quarterly-tax-deadlines/)
- [AI Estimated Tax Filing 2025 — Open Ledger](https://www.openledger.com/ai-tax-software-automation/ai-estimated-tax-filing-a-guide-for-taxpayers-in-2025)
