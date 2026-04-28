# ADR-0062 — Financial Health Metrics (Stage 9)

**Status:** Accepted — implemented 2026-04-28

---

## Context

Personal finance trackers need to distill account positions, spending patterns, and future obligations into a few high-level indicators so users can quickly assess their financial health. A user wants to know: "How much can I safely spend this month?" (spendable), "How long can I live on my assets if expenses don't change?" (runway), and "Am I trending toward or away from my goals?" (burn rate and savings rate).

These metrics are derived from transaction history and account balances. Computing them involves filtering, aggregating, and normalization — all done on request, never stored as columns. This keeps the data model normalized and avoids stale state problems.

The metrics also need to handle incomplete data gracefully. A user might have only one week of transaction history, or no budget at all. Returning a computed zero (e.g., "0% savings rate") when the input is missing would be misleading. Instead, null signals "not enough data," which is honest and disappears once history accumulates.

---

## Decision

Add four financial health metrics, all computed on request, all nullable.

### Metric 1: Spendable

**Formula:** `SUM(balances of non-excluded asset accounts) − SUM(EstimatedAmount of qualifying recurring transactions due this calendar month)`

An asset account's balance is included unless the account is flagged with `ExcludeFromSpendable = true` OR the account type is `Liability` (liability accounts are always excluded regardless of the flag).

A recurring transaction qualifies if:
- It is active and not paused.
- Its `NextDueDate` falls within the current calendar month.

**Why NextDueDate filter:** Once a recurring transaction is confirmed (marked as realized), `NextDueDate` advances to the next month automatically. This prevents double-counting against a spendable balance that already reflects the confirmed payment. Unconfirmed recurring transactions with a due date in the past remain in the calculation until manually confirmed or dismissed.

**Why liability accounts are always excluded:** A liability balance represents debt owed, not available funds. A user's spendable amount should never include borrowed money, even if they have not flagged the liability for exclusion.

**Why the ExcludeFromSpendable flag exists:** Some asset accounts (e.g., a locked savings account, a pension fund, a college fund with withdrawal restrictions) have balances that are not accessible for day-to-day spending. The flag lets users opt those accounts out of the spendable calculation while keeping them in net worth reports. This is an account-level decision, not a metric-level one.

**Returns null when:** No asset accounts exist OR all asset accounts are excluded OR no recurring transactions are due.

### Metric 2: Runway

**Formula:** `(total assets − total liabilities) ÷ average monthly expenses (last 6 full calendar months)`

Total assets = SUM of all active `Account` balances where `AccountType.AccountCategory == Asset`.
Total liabilities = SUM of all active `Account` balances where `AccountType.AccountCategory == Liability` (as positive values).
Average monthly expenses = `SUM(Transaction.Amount for all Expense transactions in the 6-month window) ÷ 6`.

The 6-month window includes the six completed calendar months immediately prior to the current month. The current month is excluded because it is incomplete and would understate the average.

For example, on 2026-04-15:
- Window is 2025-10-01 through 2026-03-31 (October, November, December, January, February, March).
- 2026-04 is excluded.

**Why 6 full calendar months (not including the current month):** Provides a stable trailing window with enough history to smooth month-to-month volatility. Including the current partial month would bias the average toward the current spending pace, which is not yet representative. Excluding recent months entirely (e.g., only looking at 2025) would ignore seasonal changes in expense patterns.

**Why not store the computed value:** All health metrics are derived on request — storing them would require periodic updates and create stale state problems. A user's runway changes daily as new transactions are added and account balances shift. Recomputing on each request is more reliable than maintaining a cached column.

**Returns null when:**
- Average monthly expenses = 0 (no expenses recorded in the 6-month window).
- No transactions exist in the 6-month window.
- Net worth (assets − liabilities) is negative and expenses are positive (mathematically undefined; a negative net worth with ongoing spend means no runway).

Returning null is more honest than returning infinity (when net worth > 0 but expenses = 0) or a very large number. Null means "not enough data" — a transient state.

### Metric 3: Burn Rate

**Formula:** `(sum of all Expense transactions this calendar month) ÷ (total assets − total liabilities)`

Expressed as a percentage: `(expenses ÷ net worth) × 100%`.

A burn rate of 5% means the user is spending 5% of their net worth per month at the current pace.

**Why this calendar month:** Captures the current spending trend. The month is incomplete, but the user benefits from an up-to-date signal.

**Returns null when:** Total net worth ≤ 0 (division by non-positive number is undefined or economically meaningless) OR no expense transactions exist this month.

### Metric 4: Savings Rate

**Formula:** `(SUM(Income transactions this calendar month) − SUM(Expense transactions this calendar month)) ÷ SUM(Income transactions this calendar month)`

Expressed as a percentage. A savings rate of 25% means 25% of income is retained (not spent) this month.

**Why this calendar month:** Captures the current savings trend. The month is incomplete, but the user benefits from an up-to-date signal.

**Returns null when:** Total income = 0 (division by zero) OR no income transactions exist this month.

### Null handling

All four metrics return nullable (e.g., `decimal?` in C#, `number | null` in TypeScript).

Null does not mean zero. Null means "insufficient data to compute this metric."
- Zero is a valid computed value (e.g., runway = 0 means net worth equals zero; burn rate = 0% means no expenses this month).
- Null is returned when computation is not meaningful (e.g., no income to divide by, no assets to assess).

**UI rendering:**
- If a metric is null, the UI renders a dash (`—`) with a help tooltip: "Not enough data."
- If a metric is zero, the UI renders the numeric value (e.g., "0%", "0 days").

**Rationale:** Forcing a fallback like 0 for metrics that have not yet been computed would be misleading. A 0% burn rate when no budgets exist looks like a goal achieved (spending is under control) rather than a missing input (no budgets were set up). Null makes the absence of data explicit.

### Scope

These metrics apply only to transactions and accounts in the user's home currency (Phase 1 scope). Multi-currency support is deferred to Phase 3. Reports and dashboards that display health metrics filter their transaction and account data by currency before computing metrics.

---

## Alternatives Rejected

**Store computed metrics as columns:** Creates stale state. A user's runway changes daily; a column would require nightly or real-time recomputation. This is a maintenance burden and a source of bugs. Computing on request is simpler and always current.

**Return 0 or a default value instead of null:** Misleading. A user with no transactions yet would see "0% savings rate," which looks like a valid result rather than missing input. Null is semantically correct.

**Use a shorter window for runway (e.g., 3 months):** Increases month-to-month volatility. 6 months balances stability with recency. Shorter windows are deferred as a user preference in Phase 3.

**Include the current partial month in the 6-month window:** Biases the average toward the current pace, which is not yet representative. The current month should be treated separately (e.g., for burn rate and savings rate, which benefit from an up-to-date signal, but excluded from the runway historical baseline).

---

## Consequences

- New `IFinancialHealthService` interface with four methods: `GetSpendableAsync()`, `GetRunwayAsync()`, `GetBurnRateAsync()`, `GetSavingsRateAsync()`. All return nullable decimals or return null directly.
- Metrics are computed in-memory after fetching accounts and transactions. No new database queries or indexes are required.
- Dashboard and reports pages call the service methods to populate health metric tiles. Tiles render null as `—` with a tooltip.
- `AccountService.GetAccountsAsync()` and related queries are enhanced to support filtering by `ExcludeFromSpendable` flag and account type.
- `RecurringTransactionService` already provides `NextDueDate`; no changes required to the recurring transaction layer.
- Transaction queries already support filtering by date and category type; no changes required.
- UI components for health metric tiles are added to the React client (if the client displays the dashboard; this is a Razor template in Phase 2, so no React changes required yet).
- No migration required; no new columns or tables are added.
- All four metrics are unit-testable without database access — the service receives accounts and transactions as parameters.
