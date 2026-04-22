# ADR 0050: Tax Reserve Envelopes

## Status: Accepted

## Context

Freelancers mentally earmark a portion of their account balance for quarterly tax
obligations (IRPF, IVA). The dashboard currently counts all asset balances as spendable —
including money set aside for taxes. This creates a misleading picture of available funds.

Three modelling options were considered:

- **Virtual sub-account** — a special account type for reserved amounts. Adds a new
  account type and complicates balance calculations.
- **Reserved-amount field on Account** — a nullable `TaxReserveAmount` on the account
  itself. Couples tax logic to the Account entity.
- **Dedicated reserve account via transfers** — user creates a "Tax Reserve" asset account
  and transfers money into it. No new schema. Net worth is unaffected (asset moves between
  two asset accounts). Dashboard can exclude this account from the spendable view.

The third option was chosen: it uses the existing transfer model, requires no new entity,
and is conceptually honest — the money is genuinely set aside, not just flagged in place.

## Decision

**Tax reserves are modelled as a dedicated asset account funded by transfers.**
`ExcludeFromSpendable bool NOT NULL DEFAULT false` is added to `Account`.

### How it works

1. User creates a "Tax Reserve" (or similarly named) asset account
2. User sets `ExcludeFromSpendable = true` on that account — via a checkbox on the
   account create/edit form: "Exclude this account from spendable balance"
3. When tax money is mentally set aside, user transfers funds into the reserve account
4. Dashboard shows two figures:

```
Total assets:        €6,500
  (Tax Reserve:      €1,500  ← excluded, shown for transparency)
Spendable balance:   €5,000
Net worth:           €6,500  ← unaffected
```

### Schema addition

```
Account
  + ExcludeFromSpendable  bool NOT NULL DEFAULT false
```

### Spendable balance formula

```
Spendable balance = SUM(balance) WHERE AccountType = Asset AND ExcludeFromSpendable = false
```

Net worth continues to use all asset and liability accounts regardless of the flag.

### Not tax-specific

`ExcludeFromSpendable` is not named or scoped to tax reserves — it is a general account
flag. Users may apply it to any account they wish to exclude from the spendable view
(e.g. a long-term savings account, an investment fund). The tax reserve use case is the
primary motivation, but the mechanism is general-purpose.

## Consequences

**Positive:**
- Zero new entity — the existing transfer model handles fund movement
- Net worth is always accurate — reserved money is still an asset
- General-purpose flag works for any "set aside" use case, not just taxes
- Transfer history provides a full audit trail of how much has been reserved over time

**Negative:**
- Requires the user to create and manage a separate account — slightly more setup than a
  simple "reserve amount" field
- The excluded account still appears in the account list — users must understand it is
  intentionally separate
