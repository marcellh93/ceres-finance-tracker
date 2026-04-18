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

## Maybe / Future Consideration

These ideas have merit but are not assigned to a phase yet. Revisit when the app is in daily use.

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
