# Business Model

> This document is a living file. The broad structure is defined here based on early planning decisions,
> but pricing, cost analysis, and tier details will be fleshed out in Phase 5 — once the product
> is stable, hosted, and has been validated with real users.

## Index

1. [Approach](#approach)
2. [Tier Structure](#tier-structure)
3. [Cost Drivers](#cost-drivers)
4. [Open Questions](#open-questions)

---

## Approach

**Freemium.** The core personal finance features are free. Premium features — specifically those
that carry a per-user infrastructure cost — are gated behind a paid tier. The goal is for
subscription revenue from premium users to cover the cost of running those features.

This model was chosen because:
- It keeps the app accessible to individuals who just want to track their finances
- Bank connectivity has a real per-user cost (third-party open banking providers charge per
  connected account), so it cannot be offered for free at scale
- Autónomo-specific features (IVA tracking, tax reports) serve a narrower audience who
  derive direct financial/tax value from them — a natural fit for a paid tier

---

## Tier Structure

### Free Tier

Available to all users at no cost.

| Feature | Included |
|---------|----------|
| Accounts (manual) | Yes |
| Transactions (manual entry) | Yes |
| File attachments on transactions | Yes |
| Categories | Yes |
| Dashboard | Yes |
| All Phase 1 & 2 reports | Yes |
| CSV / OFX file import | Yes |
| CSV export | Yes |
| Recurring transactions | Yes |
| Saved report configurations | Yes |
| Budget targets | Yes |

### Premium Tier

Paid subscription. Pricing TBD in Phase 5.

| Feature | Included |
|---------|----------|
| Everything in Free | Yes |
| Bank connectivity (automatic transaction import via open banking) | Yes |
| Personal vs. Business flagging on accounts and categories | Yes |
| IVA / VAT tracking | Yes |
| Quarterly tax summary (Modelo 130 / 303) | Yes |
| Client tracking | Yes |
| Invoice reference on transactions | Yes |

---

## Cost Drivers

### Bank Connectivity
The primary cost that makes a free tier unsustainable for this feature.

- **Provider:** Nordigen / GoCardless (supports Spanish banks under PSD2 / EU open banking)
- **Model:** Charges per connected bank account per month
- **Implication:** Each premium user who connects a bank account generates a recurring cost —
  the subscription price must be set above this cost per user to be sustainable
- **Exact pricing:** To be confirmed in Phase 5 based on expected user volume and provider quotes

### Hosting
- Phase 3 introduces hosting costs (server, database, storage for file attachments)
- These are fixed or near-fixed costs at low user volumes — to be evaluated in Phase 5

---

## Open Questions

- [ ] What is the subscription price for the Premium tier? (To be decided in Phase 5 based on cost analysis)
- [ ] Monthly vs. annual billing, or both?
- [ ] Is there a trial period for Premium features?
- [ ] Which payment processor to use? (Stripe is the most common choice for this type of app)
- [ ] Are autónomo features fully Premium, or is a subset available on the Free tier?
