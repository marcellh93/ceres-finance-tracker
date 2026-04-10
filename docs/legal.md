# Legal Obligations

> Last reviewed: 2026-04-10. Review annually or when GDPR guidance changes.

> This is a living document. Update it as new features are planned or regulations change.
> Nothing here is a substitute for qualified legal advice — consult a lawyer before Phase 3 launch.

## Index

1. [Applicable Regulations](#applicable-regulations)
2. [GDPR Obligations](#gdpr-obligations)
3. [Data Retention Policy](#data-retention-policy)
4. [User Rights](#user-rights)
5. [Required Documents](#required-documents)
6. [Phase-by-Phase Legal Checklist](#phase-by-phase-legal-checklist)
7. [Open Legal Questions](#open-legal-questions)

---

## Applicable Regulations

| Regulation | Scope | Applies from |
|------------|-------|--------------|
| **GDPR** (EU 2016/679) | Any app processing personal data of EU residents | Phase 3 — first external user |
| **LOPDGDD** (Spain, Ley Orgánica 3/2018) | Spanish national implementation of GDPR | Phase 3 |
| **Código de Comercio** (Spain) | Financial record retention — 6 years minimum | Phase 1 (your own data) |
| **Ley General Tributaria** (Spain) | Tax-relevant record retention — 4 years (6 recommended) | Phase 1 (your own data) |
| **PSD2** (EU Directive 2015/2366) | Open banking / bank connectivity | Phase 4 — bank connectivity feature |
| **PCI DSS** | Payment card data security | Phase 5 — only if handling card payment data directly |

---

## GDPR Obligations

### Legal Basis for Processing
Every category of personal data processed must have a documented legal basis under GDPR Article 6.

| Data | Legal Basis | Notes |
|------|-------------|-------|
| Account registration data (email, name) | Contractual necessity | Required to provide the service |
| Financial transaction data | Contractual necessity | Core purpose of the app |
| Usage/audit logs | Legitimate interest | Must be balanced against user privacy — document the assessment |
| Bank connection data (Phase 4) | Contractual necessity + explicit consent | Sensitive — requires clear opt-in |
| Billing data (Phase 5) | Contractual necessity | Only if handling payments directly |

### Data Minimisation
Only collect data that is necessary for the stated purpose. Do not add data fields speculatively.

### Security
- Passwords must be hashed (never stored in plain text) — use bcrypt or Argon2
- Data in transit must use HTTPS/TLS
- Database access must be restricted and credentials stored securely (not in source code)
- File attachments must not be publicly accessible without authentication

### Data Breach Notification
- GDPR requires notifying the relevant supervisory authority within **72 hours** of becoming aware of a breach
- In Spain the authority is **AEPD** (Agencia Española de Protección de Datos) — aepd.es
- Affected users must also be notified without undue delay if the breach poses high risk to their rights
- A breach response procedure must be documented before Phase 3 launch

---

## Data Retention Policy

### Financial Records — Legal Minimums (Spain)

| Record type | Minimum retention | Legal basis |
|-------------|------------------|-------------|
| Accounting records / transactions | 6 years | Código de Comercio, Art. 30 |
| Tax-relevant documents (IVA, IRPF) | 4 years | Ley General Tributaria, Art. 66-70 |
| Invoices and receipts (attachments) | 4 years (6 recommended) | Ley General Tributaria |

**These records must never be auto-purged**, even if a user requests deletion. GDPR Article 17(3)(b) explicitly exempts legally required retention from the right to erasure.

### Application Data — Retention and Purge Schedule

| Data | Retention | Action |
|------|-----------|--------|
| Soft-deleted SavedReports | 90 days from `DeletedAt` | Auto-purge after 90 days |
| Audit logs (Phase 3+) | 6 months from creation | Auto-purge after 6 months |
| Deactivated accounts/categories | Indefinite | Never purge — required for historical integrity |
| User account (upon account closure) | Anonymise immediately, retain financial records for legal period | Replace personal identifiers with anonymous tokens |

---

## User Rights

Under GDPR, users have the following rights. Each must have a working implementation before Phase 3 launch.

| Right | What it means | Implementation notes |
|-------|---------------|----------------------|
| **Right of access** | User can request all data held about them | Export all user data as a downloadable file |
| **Right to rectification** | User can correct inaccurate personal data | Already handled via normal edit flows |
| **Right to erasure** | User can request deletion of their account and data | Delete personal identifiers; retain financial records for legal period with anonymisation |
| **Right to portability** | User can export their data in a machine-readable format | CSV export covers this — verify it is complete |
| **Right to object** | User can object to processing based on legitimate interest | Relevant for audit logs — provide opt-out or honour objection |
| **Right to restriction** | User can request processing be limited while a dispute is resolved | Mark account as restricted in the system |

---

## Required Documents

These must be written, published, and accessible before any user outside yourself can access the app.

| Document | Required from | Notes |
|----------|--------------|-------|
| **Privacy Policy** | Phase 3 | Must cover: what data is collected, why, how long it is kept, user rights, contact for requests, supervisory authority (AEPD) |
| **Terms of Service** | Phase 3 | Defines acceptable use, liability limitations, account termination conditions |
| **Cookie Policy** | Phase 3 (if cookies used) | If using session cookies or analytics — must allow opt-out of non-essential cookies |
| **Data Processing Agreement (DPA)** | Phase 3 | Required with any third-party processor (hosting provider, email service, etc.) |
| **Data Breach Response Procedure** | Phase 3 | Internal document — who is responsible, what steps to take, how to notify AEPD |

---

## Phase-by-Phase Legal Checklist

### Phase 1 & 2 (Local — single user)
- [ ] Understand your personal financial record retention obligations (Código de Comercio, 6 years)
- [ ] No GDPR obligations yet — only your own data

### Phase 3 (Hosted Beta — first external users)
- [ ] Privacy Policy written and published
- [ ] Terms of Service written and published
- [ ] Cookie Policy in place (if applicable)
- [ ] Legal basis for all data processing documented
- [ ] Data breach notification procedure documented
- [ ] All user rights implemented (access, erasure, portability, etc.)
- [ ] Data Processing Agreements signed with hosting provider and any third-party services
- [ ] HTTPS enforced — no unencrypted connections
- [ ] Passwords hashed with Argon2
- [ ] MFA enforced for all users via TOTP authenticator app (no SMS)
- [ ] Auto-purge for soft-deleted records and audit logs implemented and tested
- [ ] Right to erasure flow tested — confirm financial records are retained, personal identifiers anonymised

### Phase 4 (Bank Connectivity — PSD2)
- [ ] Confirm Nordigen/GoCardless DPA covers GDPR requirements
- [ ] Review PSD2 obligations for storing or passing bank credentials
- [ ] Update Privacy Policy to cover bank data processing
- [ ] Explicit consent flow implemented for bank connection

### Phase 5 (Business Model — Billing)
- [ ] If handling card payments directly: PCI DSS compliance assessment
- [ ] Recommended: use a payment processor (Stripe) that handles card data so PCI DSS scope is minimal
- [ ] Update Terms of Service to cover subscription terms, refunds, cancellation

---

## Open Legal Questions

- [ ] Will the app eventually need to register as a data controller with AEPD? (Required in Spain once processing data of others at scale)
- [ ] Does the bank connectivity feature (Phase 4) require any licensing under PSD2 as an AISP (Account Information Service Provider)?
- [ ] What jurisdiction's law governs the Terms of Service if the user base becomes international?
