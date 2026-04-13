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

## Phase 5 — Business Model

**Gate: Phase 4 must be stable. See [`business-model.md`](business-model.md) for full detail.**

Goal: make the product self-sustaining via a freemium tier structure.

- **Free tier:** core personal finance tracking (manual entry, file import, reports)
- **Premium tier:** bank connectivity and autónomo features
- Authentication and multi-tenancy from Phase 3 are prerequisites for any billing system
- **Passkeys / WebAuthn** — optional alternative to password + MFA for maximum security. Private key stays on the user's device; server holds only the public key. Adds significant implementation complexity — only appropriate once the Phase 3 auth foundation is stable.
- **Paid restoration service** — when a user who churned naturally (no erasure request) returns after their grace period but before permanent deletion, restoring their archived data is a paid service. Fee set at archive creation time. Users who submitted a GDPR erasure request cannot use this service. See ADR-0029 for the full lifecycle and `CustomerArchive` schema. Do not build before Phase 5.
