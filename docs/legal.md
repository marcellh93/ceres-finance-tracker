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
7. [EU Accessibility Act](#eu-accessibility-act)
8. [Open Legal Questions](#open-legal-questions)

---

## Applicable Regulations

| Regulation | Scope | Applies from |
|------------|-------|--------------|
| **GDPR** (EU 2016/679) | Any app processing personal data of EU residents | Phase 3 — first external user |
| **LOPDGDD** (Spain, Ley Orgánica 3/2018) | Spanish national implementation of GDPR | Phase 3 |
| **Código de Comercio** (Spain) | Financial record retention — 6 years minimum | Phase 1 (your own data) |
| **Ley General Tributaria** (Spain) | Tax-relevant record retention — 4 years (6 recommended) | Phase 1 (your own data) |
| **EU Accessibility Act** (Directive 2019/882) | Accessibility requirements for digital products and services — private sector obligations apply from 28 June 2025 | Phase 3 — obligations are already active as of April 2026. See [EU Accessibility Act](#eu-accessibility-act) section below for full analysis. |
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
- Passwords must be hashed (never stored in plain text) — use Argon2id with explicitly pinned parameters: m=19456 (19 MB memory), t=2 iterations, p=1 parallelism (OWASP minimum baseline). Do not use bcrypt for new implementations — Argon2id is the current standard
- TOTP shared secrets (the seed generated during MFA setup) must be stored encrypted at rest — plaintext storage nullifies MFA protection if the database is dumped
- The application's runtime database user must have DML rights only (SELECT, INSERT, UPDATE, DELETE) — a separate migration-only role holds schema modification rights
- Data in transit must use HTTPS/TLS
- Database access must be restricted and credentials stored securely (not in source code)
- File attachments must not be publicly accessible without authentication
- All state-changing forms must use CSRF anti-forgery tokens — ASP.NET Core's built-in anti-forgery middleware handles this via `[ValidateAntiForgeryToken]` on controllers and tag helper `<form>` elements
- HTTP security headers must be set on all responses: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, and HSTS once HTTPS is enforced. A Content Security Policy header must be defined before any JavaScript is introduced.

### Record of Processing Activities (RoPA) — GDPR Art. 30

> **Status: DRAFT scaffold (2026-10-05) — NOT legally reviewed.** The processing facts below are grounded in the codebase as of this date; `[CONFIRM]` marks a fact only the controller/counsel can supply (identity, subprocessors, legal-basis wording). Review and complete before Phase 3 launch. Keep this current: a new data field, a new subprocessor, or a new purpose means a new/updated row here in the same change.

**Controller.** `[CONFIRM: legal entity name]`, `[CONFIRM: NIF]`, `[CONFIRM: registered address]`, `[CONFIRM: controller contact email]`. DPO: `[CONFIRM: DPO name + contact, or a documented statement that no DPO is appointed and why]`.

**Scope note.** Single application, single controller. No special-category data (GDPR Art. 9) is collected — verified: no health, biometric, ethnic, religious, political, genetic, or sexual-orientation fields exist in the data model. No automated decision-making or profiling with legal/similarly-significant effect (Art. 22). No children's data is knowingly processed — `[CONFIRM: age gate / terms position]`.

Each processing activity below is one Art. 30(1) record: purpose, data categories, data subjects, legal basis, retention, recipients.

| # | Processing activity | Purpose | Personal data categories (grounded in the data model) | Data subjects | Legal basis (Art. 6) | Retention | Recipients / subprocessors |
|---|---|---|---|---|---|---|---|
| P1 | Account + authentication | Create and secure a user account | Email (`AspNetUsers`), password hash (Argon2id), TOTP secret (encrypted), pending email change (`EmailChangeToken.NewEmail`) | Registered users | `[CONFIRM: contract — service delivery]` | Until erasure / natural-churn schedule (see Retention Policy) | None today (self-hosted). `[CONFIRM: hosting provider]` |
| P2 | Core finance data | The product's purpose — track accounts, transactions, budgets, categories, transfers, recurring, saved reports, imported data | User-entered financial data; attachment file names + files on the filesystem (`StoredPath`) | Registered users | `[CONFIRM: contract — core purpose]` | Financial records 6 years (Código de Comercio) — survives erasure (Art. 17(3)(b)); other app data per Retention Policy | None today. `[CONFIRM: hosting provider]` |
| P3 | Security + abuse logging | Detect/prevent unauthorised access, rate-limit, audit | IP address + user-agent (`UserSession`, `FailedLoginAttempt`, `AuditLog`, `UserBlockedIp`), attempted email on failed login | Users + anonymous visitors hitting auth endpoints | `[CONFIRM: legitimate interest — security; document the Art. 6(1)(f) balancing test]` | Failed-login logs 1 year; audit logs 12 months (auto-purge) | None today |
| P4 | Transactional email | Send verification, password-reset, email-change, export-ready, erasure, security notices | Email address, the message content (contains name/links) | Registered users | `[CONFIRM: contract — service-related messages]` | Not retained by us beyond send (delivery is stateless); `EmailDeliveryEvent` metadata per its own retention | **No external email processor wired today** (`NoopEmailService`). `[CONFIRM: email service provider + DPA]` before enabling real send |
| P5 | Support correspondence | Handle user support tickets | Ticket subject/body (may contain PII the user types), attachment file names + files, email for notifications | Users who open tickets | `[CONFIRM: contract + legitimate interest]` | Redact-retained after erasure (identifiers scrubbed, conversation kept on Art. 6(1)(f)); otherwise `[CONFIRM: active-ticket retention]` | None today. `[CONFIRM: hosting provider]` |
| P6 | Data export (portability) | Fulfil GDPR Art. 20 / Art. 15 requests | A ZIP of the user's own content + profile, delivered via time-limited single-use link | Registered users who request it | `[CONFIRM: legal obligation — Art. 15/20]` | Export ZIP deleted at 24h TTL (or on erasure); request logged in audit | None today |
| P7 | Erasure processing | Fulfil GDPR Art. 17 requests | Identity fields (anonymised), pseudonymised erasure audit record | Registered users who request it | `[CONFIRM: legal obligation — Art. 17]` | Pseudonymised erasure record 3 years (accountability); financial records retained per legal minimum | None today |

**International transfers.** None today — all data is stored on the controller's own infrastructure (`[CONFIRM: hosting region — must be EU/EEA, or document the Art. 44–49 transfer mechanism`]). Once a hosting provider or email subprocessor is engaged, add its location + transfer safeguard here. No third-party analytics or tracking (confirmed: none in the codebase; `planning-resolved.md` records no Phase-3 analytics).

**Technical & organisational measures (Art. 32)** are documented in `security-model.md` and summarised under § Security above (Argon2id, encrypted TOTP secrets, least-privilege DB role, TLS, CSRF, security headers, RLS multi-tenant isolation, filesystem-not-BLOB attachments).

### Data Breach Notification — Response Runbook

> **Status: DRAFT scaffold (2026-10-05) — NOT legally reviewed.** The GDPR timeline + AEPD facts are correct; roles, contacts, and tooling are `[CONFIRM]`. Complete and rehearse before Phase 3 launch. This runbook satisfies the "Data Breach Response Procedure" Required Document.

**Legal spine (fixed facts):**
- Notify the supervisory authority within **72 hours** of becoming aware of a breach (GDPR Art. 33). In Spain the authority is **AEPD** (Agencia Española de Protección de Datos) — aepd.es; notification is filed through the AEPD electronic registry (`[CONFIRM: exact AEPD notification channel/URL at launch]`).
- Notify **affected users** without undue delay if the breach poses a **high risk** to their rights/freedoms (Art. 34). The high-risk assessment decides whether user notice is required — document it either way.
- Breach = any accidental/unlawful destruction, loss, alteration, unauthorised disclosure of, or access to personal data (not only "data leaked externally").

**Roles (`[CONFIRM]` — assign real people before launch):**
- **Incident lead** — `[CONFIRM]`: owns the response, makes the notify/don't-notify call, single point of coordination.
- **Technical responder** — `[CONFIRM]`: contains the breach, preserves evidence/logs, scopes affected data.
- **Notifier** — `[CONFIRM]`: files the AEPD notification and drafts user notices (the incident lead may be the same person at this scale).
- **DPO** — `[CONFIRM: DPO or "no DPO; incident lead holds the decision"]`.

**The procedure:**

1. **Detect & record (clock starts here).** The 72-hour clock starts at *awareness*, not at breach occurrence. Immediately log: timestamp of awareness, who reported it, one-line description. Open an incident record (`[CONFIRM: where — ticket/issue tracker/secure doc]`).
2. **Contain.** Technical responder stops ongoing exposure — revoke compromised credentials/sessions (the app has session revocation + `SealedAt`; use it), rotate secrets, block the vector, take the affected component offline if needed. Preserve logs (`AuditLog`, `FailedLoginAttempt`, host/access logs) before anything is cleaned up — they are the evidence and the scoping source.
3. **Assess scope & risk.** Determine: which data categories (use the RoPA above), how many data subjects, whether identifiers + financial data were involved (higher risk), whether data was encrypted/pseudonymised (lowers risk). Write the Art. 34 high-risk determination with its reasoning.
4. **Notify AEPD (≤72h).** If the breach is reportable (any risk to rights, unless unlikely), file with AEPD within 72 hours of awareness. Content (Art. 33(3)): nature of the breach, categories + approximate number of subjects and records, likely consequences, measures taken/proposed, DPO or contact point. If full detail isn't ready, file an initial notification and supplement — do not miss the 72h deadline waiting for completeness.
5. **Notify affected users (if high risk, Art. 34).** Clear, plain-language notice: what happened, likely consequences, what you're doing, what they should do (e.g. change password), and a contact point. `[CONFIRM: user-notification channel — the transactional email path (P4) once a real email provider is wired; today email send is Noop]`.
6. **Remediate & close.** Fix the root cause, verify containment, update the incident record with the full timeline and lessons. If measures from `security-model.md` were missing or insufficient, update that doc + this runbook in the same pass.
7. **Post-incident.** Retain the incident record for accountability (`[CONFIRM: retention period, min. aligned with audit-log/accountability horizons]`). Review whether the DPIA or RoPA needs updating.

**Pre-launch readiness checklist (what makes this runbook real):**
- [ ] Roles assigned to named people with contact details (`[CONFIRM]`)
- [ ] AEPD notification channel identified and an account/path confirmed (`[CONFIRM]`)
- [ ] Incident-record location chosen and access-controlled (`[CONFIRM]`)
- [ ] A dry-run/tabletop walkthrough completed once before Phase 3 launch
- [ ] User-notification email path operational (depends on a real email provider replacing `NoopEmailService`)

### Data Protection Impact Assessment (DPIA) — GDPR Art. 35

> **Status: DRAFT scaffold (2026-10-05) — NOT legally reviewed.** The processing description, risk sources, and existing measures below are grounded in the codebase + the RoPA above. The **necessity/proportionality judgments and the residual-risk acceptance are controller/counsel calls** and are left as `[CONFIRM]` — a DPIA's conclusions cannot be auto-generated. `security-model.md` § Article 35 holds the policy on *when* a DPIA is required; this is the document itself. Complete it with the **AEPD Gestiona EIPD** tool (per `security-model.md`) before Phase 3 launch; redo it at Phase 4 (bank connectivity / any automated scoring).

**1. Is a DPIA required?** Yes (advisable→required for this launch). Financial data meets the EDPB "sensitive nature / highly personal" criterion (WP248rev.01); combined with large-scale processing of EU residents' identity + financial data, the threshold is met. `[CONFIRM: counsel's formal necessity determination]`.

**2. Systematic description of the processing.** See § RoPA above (7 activities). In summary: a single-tenant-per-user financial tracker storing user-entered financial data + identity (email) + security telemetry (IP/user-agent), self-hosted, no third-party analytics, no special-category (Art. 9) data, no Art. 22 automated decision-making. Data subjects: registered EU beta users. `[CONFIRM: approximate scale at launch — number of users]`.

**3. Necessity & proportionality.** `[CONFIRM WITH COUNSEL]`: assess that each purpose (RoPA P1–P7) is necessary for the service and that no less-intrusive means achieves it. Data-minimisation is designed in (see § Data Minimisation; no speculative fields) — counsel confirms the proportionality conclusion.

**4. Risks to data subjects + existing measures.** The risk sources below are real; the likelihood/severity ratings and residual-risk acceptance are `[CONFIRM]`.

| Risk to data subjects | Source | Existing measure (grounded in the build) | Likelihood × severity → residual |
|---|---|---|---|
| Unauthorised access to another user's financial data (cross-tenant leak) | Multi-tenant DB | PostgreSQL **Row-Level Security** on every `IUserOwned` table + the app runs as a non-`BYPASSRLS` role; integration tests assert cross-user reads return 403/404 | `[CONFIRM: rating]` |
| Credential compromise | Auth | **Argon2id** password hashing (pinned params), **TOTP MFA** with encrypted-at-rest secrets + replay prevention, session revocation, lockout, breached-password check | `[CONFIRM: rating]` |
| Data exposure in transit / at the host | Transport / infra | TLS/HTTPS enforced; attachments on filesystem (not world-readable, auth-gated), not BLOBs; least-privilege DB runtime role (DML-only) | `[CONFIRM: rating]` |
| Excess retention / failure to delete | Retention | Automated retention purges (audit 12mo, failed-login 1yr, soft-deletes 90d) + Art. 17 erasure flow with pseudonymised accountability record | `[CONFIRM: rating]` — note the purge **cron is not scheduled until Stage 16**; until then enforcement is manual (a real residual risk to record) |
| Breach not detected / not notified in time | Detection / response | Audit + failed-login logging; breach runbook (§ above) with the 72h/AEPD procedure | `[CONFIRM: rating]` |
| Over-collection via support tickets (free-text PII) | Support | Redact-retain on erasure (`IdentifierRedactor`); `[CONFIRM: any intake-time minimisation guidance]` | `[CONFIRM: rating]` |
| Third-party processor risk | Subprocessors | **None engaged today** (self-hosted; email is `NoopEmailService`). Once hosting/email providers are added, each needs a DPA (Art. 28) before go-live | `[CONFIRM: rating once processors chosen]` |

**5. Measures to address the risks / reduce residual risk.** Existing measures are in column 3 above and `security-model.md`. `[CONFIRM WITH COUNSEL: any additional measures required to bring each residual risk to acceptable — e.g. schedule the retention cron before launch rather than deferring to Stage 16; sign DPAs before engaging processors]`.

**6. Outcome.** `[CONFIRM WITH COUNSEL]`: the residual-risk acceptance decision, sign-off, and date. **File the outcome with the DPO if appointed** (`[CONFIRM: DPO, or documented statement none is appointed]`). Re-run triggers: Phase 4 bank connectivity, any automated scoring/profiling, any new special-category data, any new subprocessor materially changing the risk profile.

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
| Audit logs (Phase 3+) | 12 months from creation | Auto-purge after 12 months |
| Failed-login logs | 1 year from creation | Auto-purge after 1 year |
| GDPR erasure-request record | 3 years (accountability) | Keep the anonymised erasure record; the referenced personal data is deleted at erasure time |
| Deactivated accounts/categories | Indefinite | Never purge — required for historical integrity |
| User account — natural churn (no erasure request) | 30-day grace period, then sealed archive for 150 days, then permanent deletion at 180 days | Grace: data intact, free reactivation. Archive: compressed file on filesystem, restoration available at a cost. Day 180: archive file deleted, no restoration possible. |
| User account — GDPR erasure request (Art. 17) | Anonymise personal identifiers immediately | Replace name, email, and direct identifiers with anonymous tokens. Retain anonymised financial records for legal period. No archive created — restoration is not possible. A 72-hour cancel-only hold precedes anonymisation: the account is sealed (login refused) but reversible via a one-time emailed link. After 72 hours the hold is non-restorable — distinct from the natural-churn archive above, which stays restorable for its full 150-day window. |
| Support correspondence (post-erasure) | Indefinite, redacted | Ticket/message content is redacted of identifying details (name, email, account references) but the conversation itself is retained on a legitimate-interest basis (GDPR Art. 6(1)(f)) — troubleshooting-pattern and product-quality value outweighs the minimal residual privacy impact once identifiers are stripped. Not financial-record-statutory retention (different legal basis from the Financial Records table above). |

> **Policy disclosure requirement:** The 30-day grace period and 180-day archive window are product policy, not legal minimums. They must be stated explicitly in the Privacy Policy before Phase 3 launch. If these windows change, the Privacy Policy must be updated before the change takes effect.

> **Reconciliation note (Decision C, 2026-10-06):** This churn lifecycle is triggered by an **explicit account closure** (`ClosureType` recorded at close time, per ADR-0029) — NOT by inactivity. It is distinct from the *inactivity* policy in `security-model.md` § Data Retention (12mo no-login → warning, 18mo → anonymize-in-place), which is the one Stage 13.b builds. **This closure/archive lifecycle is deferred:** no account-closure trigger exists in code today (`ClosureType`/`CustomerArchive` are on-paper only, per ADR-0029), and the paid-restoration half is explicitly Phase 5. It is scoped to whenever an account-closure feature is built, not to Stage 13.b. See `planning-resolved.md` § "Stage 13.b trigger/mechanism".

> **Closure type must be recorded at account closure time.** Natural churn and GDPR erasure have different downstream rules. A user who submitted an erasure request must never have a restoration archive created, even if they later change their mind. See ADR-0029 for the `CustomerArchive` schema and lifecycle.

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
- [ ] Register brand name as a trademark with OEPM (Oficina Española de Patentes y Marcas) once the final product name is decided — approximately EUR 150–200, processing takes several months. File before public launch to establish priority date.
- [ ] Privacy Policy written and published
- [ ] Terms of Service written and published
- [ ] Cookie Policy in place (if applicable)
- [~] Legal basis for all data processing documented — scaffolded in § RoPA (per-activity legal-basis column) + § Legal Basis for Processing table; **counsel must confirm each `[CONFIRM]` basis** before `[x]`.
- [~] Data breach notification procedure documented — **runbook scaffold drafted** in § Data Breach Notification — Response Runbook (2026-10-05); not `[x]` until roles/contacts/AEPD channel filled and a tabletop walkthrough is done.
- [~] Record of Processing Activities (RoPA, Art. 30) — **scaffold drafted** in § Record of Processing Activities (2026-10-05); not `[x]` until controller identity, subprocessors, and legal-basis wording are confirmed by counsel.
- [~] DPIA (Data Protection Impact Assessment, Art. 35) — **scaffold drafted** in § Data Protection Impact Assessment (2026-10-05); not `[x]` until necessity/proportionality judgment, residual-risk ratings, and sign-off are completed by counsel (via AEPD Gestiona EIPD).
- [ ] All user rights implemented (access, erasure, portability, etc.)
- [ ] Data Processing Agreements signed with hosting provider and any third-party services
- [ ] HTTPS enforced — no unencrypted connections
- [ ] Passwords hashed with Argon2
- [ ] MFA enforced for all users via TOTP authenticator app (no SMS)
- [ ] TOTP shared secrets confirmed stored encrypted at rest
- [ ] TOTP replay prevention implemented — used codes tracked per user and rejected if resubmitted within their validity window
- [ ] Session management implemented: session fixation prevention (token regeneration on login), server-side invalidation on logout, user-configurable lifetime (short vs. persistent "remember me"), persistent tokens stored as hashes with rotation on each use
- [ ] Secure cookie flags configured: HttpOnly, Secure, SameSite=Strict or Lax
- [ ] IP enforcement toggle implemented with risk disclosure; IP blocking available to users from Security settings page
- [ ] Active session list and per-session revocation available to users on Security settings page
- [ ] Privacy policy updated to disclose persistent session data retention period
- [ ] CSRF anti-forgery tokens applied to all state-changing forms and verified by controllers
- [ ] HTTP security headers configured: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, HSTS, and CSP
- [ ] Forwarded headers middleware configured (`UseForwardedHeaders`) — required for correct HTTPS detection and real client IP resolution when behind a reverse proxy
- [ ] IDOR prevention verified — every resource endpoint confirmed to scope queries to the authenticated user; integration tests cover cross-user access attempts (must return 403 or 404)
- [ ] Password policy enforced: minimum 8 characters, breached password list check, Argon2id parameters pinned explicitly (not left at library defaults)
- [ ] Account enumeration prevention verified — login and password-reset return identical responses and timing for existing and non-existing emails
- [ ] Account-level lockout implemented — N consecutive failed login attempts trigger a temporary lock with email notification to the account owner
- [ ] CORS policy defined and restricted to known frontend origin(s) if any API endpoint is exposed cross-origin
- [ ] Dependency vulnerability scan completed before launch; automated scanning integrated into build pipeline
- [ ] Auto-purge for soft-deleted records and audit logs implemented and tested
- [ ] Right to erasure flow tested — confirm financial records are retained, personal identifiers anonymised, and `ClosureType = GdprErasure` is recorded with no archive file created
- [ ] CustomerArchive background job implemented, monitored, and tested — permanent deletion must execute on schedule at 180 days; a missed deletion job constitutes retaining data past the disclosed window
- [ ] Restoration service confirmed out of scope for Phase 3 — only archive creation and scheduled deletion are in scope at this phase
- [ ] EU Accessibility Act obligations confirmed active (28 June 2025 deadline passed) — microenterprise exemption assessed and documented if applicable
- [ ] Accessibility statement published at `/accesibilidad` with conformance status, known gaps, and enforcement contact
- [ ] `accesibilidad@[domain]` feedback inbox operational with ≤30-day SLA
- [ ] "Declaración de Accesibilidad" link in global footer
- [ ] `lang` attribute set correctly on HTML shell
- [ ] `eslint-plugin-jsx-a11y` in React ESLint config; zero critical violations in CI
- [ ] `vitest-axe` assertions added to tests for Dialog, DataTable, Combobox, DatePicker, Wizard
- [ ] All form labels associated (no placeholder-as-label); error text with `aria-describedby`
- [ ] Focus visible on all interactive elements; icon-only buttons have `aria-label`
- [ ] Dialog `<DialogTitle>` present; focus management on open/close verified
- [ ] Toast `role` differentiated by severity (status vs. alert)
- [ ] DataTable: `aria-sort`, sortable `<th>` with `<button>`, row action `aria-label`, result count `aria-live`
- [ ] Charts: `role="img"` + `aria-label` + data table alternative on every chart; `ChartWrapper` component enforces this
- [ ] Combobox ARIA pattern audited and conformant (cmdk patched or replaced if needed)
- [ ] Income/Expense and status badges: text label accompanies color coding
- [ ] Color contrast audit completed; zero critical axe contrast violations
- [ ] Reflow at 320px verified; DataTable in keyboard-accessible scroll container
- [ ] WCAG 2.1 AA compliance audit completed before opening to external users (self-certification with documented evidence minimum; third-party audit recommended)

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

## EU Accessibility Act

> Last reviewed: 2026-04-28. Review annually or when EN 301 549 is updated.

### Applicability

**Yes, the EAA applies to Project Ceres.** Directive 2019/882 covers services provided to consumers in the EU, including e-commerce and digital services (Article 2(2)(b) and (f)). A web-based personal finance SaaS offered to consumers is captured at minimum under the digital services gateway. Given the app explicitly tracks income, expenses, accounts, and financial records, Spanish authorities may classify it under the financial services scope (Annex II, Section V), which carries higher scrutiny.

**Private sector obligations are in force from 28 June 2025.** As of April 2026, Project Ceres is already operating within the enforcement window.

**Spanish transposition:** Real Decreto 1112/2018 covers only public sector bodies. The EAA's private sector obligations are transposed via **Ley 11/2023, de 8 de mayo** (BOE 09/05/2023), which amends the Ley General para la Defensa de los Consumidores y Usuarios (LGDCU, Real Decreto Legislativo 1/2007). Enforcement competence sits with the **Dirección General de Consumo** (national) and regional consumer agencies. For financial services classification, the CNMV and Banco de España may assert concurrent jurisdiction.

### SME / Microenterprise Exemption

Article 4(4) EAA exempts **microenterprises** (fewer than 10 employees AND annual turnover or balance sheet ≤ EUR 2 million, per Commission Recommendation 2003/361/EC) from the service accessibility requirements (Articles 4, 5, and 13). If the team meets this threshold, the technical conformance requirements are not legally mandatory.

**However, do not rely on this exemption as a long-term strategy:**
- The exemption disappears immediately upon crossing either threshold.
- Spanish LGDCU independently requires non-discriminatory service provision regardless of company size.
- Retroactive remediation of a production SPA is expensive.
- The exemption must be documentable on demand — it is not automatic immunity.

**Disproportionate burden (Article 14):** Non-microenterprises can invoke this for specific requirements, but the assessment must be documented per-requirement and submitted to the competent authority. It is not a blanket exemption.

### Penalties (Ley 11/2023 / LGDCU enforcement)

| Infraction | Amount |
|-----------|--------|
| Minor (missing accessibility statement, inadequate feedback mechanism) | Up to EUR 10,000 |
| Serious (service inaccessible to a class of users, insufficient conformance) | EUR 10,001 – EUR 100,000 |
| Very serious (systematic exclusion, repeated non-compliance after formal order) | EUR 100,001 – EUR 1,000,000 or 4% of annual EU turnover, whichever is higher |

Enforcement is complaint-driven. Users can file complaints via the regional consumer ombudsman system, which triggers inspection. Repeat violations after a formal correction order escalate to higher tiers.

---

### Technical Standard: EN 301 549 and WCAG 2.1 AA

The EAA mandates conformance with **EN 301 549 v3.2.1 (2021-03)**, which incorporates **WCAG 2.1 Level AA** by reference (clauses 9.1–9.4 for web content). WCAG 2.1 AA is therefore the operative technical standard.

The following criteria require active implementation work in a React SPA finance app (trivially satisfied criteria are excluded):

| Criterion | Level | Risk area in this stack |
|-----------|-------|------------------------|
| 1.1.1 Non-text Content | A | Charts (recharts SVG), Lucide icon-only buttons |
| 1.3.1 Info and Relationships | A | DataTable `<th scope>`, form field groupings, status badges |
| 1.4.1 Use of Color | A | Income/Expense badges — color cannot be the only differentiator |
| 1.4.3 Contrast Minimum | AA | Muted text, placeholders, disabled states, chart labels |
| 1.4.4 Resize Text | AA | Fixed-height containers clipping text at 200% zoom |
| 1.4.10 Reflow | AA | DataTable at 320px CSS width — must scroll within a keyboard-accessible container |
| 1.4.11 Non-text Contrast | AA | Input borders, checkbox borders, chart bars/lines vs. background |
| 1.4.12 Text Spacing | AA | Fixed-height rows clipping text under WCAG spacing overrides |
| 1.4.13 Content on Hover/Focus | AA | Recharts tooltips (not hoverable or keyboard-reachable) |
| 2.1.1 Keyboard | A | Combobox, date picker, DataTable sort/filter, modals, reconciliation dropdown |
| 2.4.3 Focus Order | A | React portals (modals) appended to body — focus order jumps |
| 2.4.4 Link Purpose in Context | A | "Edit" / "Delete" row-action buttons without row context |
| 2.4.7 Focus Visible | AA | `focus:outline-none` stripping focus rings (common in shadcn base styles) |
| 2.5.3 Label in Name | A | Icon-only buttons where `aria-label` doesn't match visible tooltip |
| 3.1.1 Language of Page | A | `<html lang="es">` missing or wrong in React SPA shell |
| 3.3.1 Error Identification | A | Validation errors shown only as red border without text |
| 3.3.2 Labels or Instructions | A | Placeholder-as-label pattern (disappears on input) |
| 3.3.3 Error Suggestion | AA | "Invalid date" instead of "Use DD/MM/YYYY format" |
| 4.1.2 Name, Role, Value | A | Custom components missing ARIA roles/states (combobox, toggle, wizard steps) |
| 4.1.3 Status Messages | AA | Toast notifications without `role="status"` or `role="alert"` |

---

### Component-Level Gap Analysis (React + shadcn/ui + Recharts)

#### shadcn/ui and Radix UI

- **Dialog:** `<DialogTitle>` is required for `aria-labelledby` — if omitted, dialog has no accessible name. On close, focus must return to the trigger; if the trigger is removed from the DOM (delete-confirmation), move focus to a meaningful fallback.
- **Combobox (Command + Popover):** cmdk does not fully implement the ARIA 1.2 combobox pattern. Missing: `role="combobox"` on input, `aria-expanded`, `aria-controls`, `aria-activedescendant`. Must be audited and patched or replaced with a conformant headless combobox (Downshift or custom APG implementation). Add a result-count announcer: `<div aria-live="polite" className="sr-only">{count} results</div>`.
- **DataTable (Tanstack Table):** sort headers need `aria-sort` attribute and a `<button>` inside `<th>` (not `onClick` on `<th>`). Row action buttons need `aria-label` with transaction context. Pagination needs `aria-live` region and accessible Previous/Next button names.
- **Toast / Sonner:** verify `<Toaster>` container uses `aria-live="polite"` for success and `role="alert"` for errors — differentiate by severity.
- **Select (Radix):** generally accessible. Verify trigger's accessible name updates to selected value after selection.
- **DropdownMenu:** verify trigger has `aria-haspopup="menu"` and `aria-expanded`. Custom trigger wrappers must not override these attributes.
- **Form / Input:** error text must be associated via `aria-describedby`. Follow the `FormField > FormItem > FormLabel + FormControl + FormMessage` pattern exactly — deviating breaks the `aria-describedby` association.

#### Recharts Charts

Every chart requires:
1. `role="img"` and `aria-label="[Chart type] of [metric]: [summary]"` on the container div
2. A visually hidden or disclosed data table alternative (`<details><summary>View data as table</summary><table>...</table></details>` or `className="sr-only"`)
3. Individual data points keyboard-focusable (`tabIndex={0}`, `aria-label="[Month]: [value]"`) via recharts `Cell` component
4. Tooltips that appear on keyboard focus, not only on hover — or a persistent data table as the primary accessible alternative
5. Non-color differentiators for multi-series charts (text labels, patterns)

Create a `ChartWrapper` component that enforces these requirements and wrap all charts in it.

#### Date Picker (Radix Popover + react-day-picker v8)

- Trigger button `aria-label` must reflect current value: `"Select date, currently 15 April 2026"` or `"No date selected"`
- Verify react-day-picker **v8** is in use (v8 has significantly better accessibility than v7)
- Month navigation buttons must have `aria-label="Go to previous month"` / `"Go to next month"`
- Keyboard: Arrow keys navigate days; Page Up/Down navigate months; Home/End navigate within week; Escape closes popover
- Test with VoiceOver + Safari — date pickers are a known failure point in that combination

#### Multi-Step Wizard (Onboarding)

- On step transition, move focus to the new step's `<h2>` heading (add `tabindex="-1"`, call `.focus()` in `useEffect` after state update)
- Add `aria-live="polite"` region announcing step transitions: `"Step 2 of 4: Enter account details"`
- Step indicator: `aria-current="step"` on current step; completed steps marked with accessible text
- On validation failure: move focus to first invalid field; error container has `role="alert"`
- Preserve all form state across back navigation

---

### Accessibility Statement Requirement

**Must be published before any external user accesses the service.**

Required content (EN 301 549 Annex C; EAA Article 13(2)):
1. Conformance status: "fully conformant", "partially conformant", or "non-conformant" with EN 301 549 / WCAG 2.1 AA
2. List of non-accessible content with reason (does not conform / disproportionate burden / out of scope) and any accessible alternatives
3. Preparation date and last review date
4. Feedback mechanism (see below)
5. Contact for enforcement: reference to the competent authority (Dirección General de Consumo or equivalent regional body)
6. Link to the enforcement / complaint procedure

**Publication location:** `/accessibility` or `/accesibilidad`, linked from the global footer on all pages as "Declaración de Accesibilidad". The statement page must itself conform to WCAG 2.1 AA.

**Update cadence:** annually at minimum; within 30 days of any significant change that affects accessibility.

**Feedback mechanism:** a dedicated email address (`accesibilidad@[domain]`) linked from the accessibility statement, with a ≤30-day SLA for substantive responses. A structured web form is preferred. The feedback channel must itself be accessible.

---

### Testing Requirements

**Automated (integrate into CI — required for baseline):**
- `eslint-plugin-jsx-a11y` in the React ESLint config — catches missing labels, invalid ARIA, icon accessibility at authoring time. Run as part of `pnpm build`; treat violations as build failures.
- `axe-core` via `vitest-axe` in Vitest component tests — add `expect(await axe(container)).toHaveNoViolations()` to tests for complex components (DataTable, Dialog, Combobox, DatePicker, Wizard). Zero `critical` and `serious` violations is the target.
- Automated coverage: ~30–40% of WCAG issues. Does not catch: logical focus order, screen reader announcement quality, meaningful accessible names, reflow behavior, content-on-hover timing.

**Manual (required before Phase 3 launch):**

Screen reader + browser combinations to test:
1. NVDA + Firefox (Windows) — most used among EU blind users; free
2. JAWS + Chrome (Windows) — enterprise standard; most common in Spanish professional settings
3. VoiceOver + Safari (macOS/iOS)
4. TalkBack + Chrome (Android)

Manual test scripts per critical flow:
- Transaction creation and edit forms (label association, error identification, submission feedback)
- Transaction / Movements DataTable (sort, filter, pagination, row actions)
- Category / account combobox selection
- Date picker (keyboard-only navigation)
- Modal open/close/confirm/cancel (focus management, trigger return)
- Onboarding wizard (step progression, error recovery, back navigation)
- Dashboard charts (accessible name, keyboard navigation, data table alternative)
- Toast notifications (screen reader announcement)

Supplementary tests:
- Keyboard-only walkthrough of every page (Tab/Shift+Tab, Enter, Escape — no mouse)
- Color contrast audit via browser DevTools and Colour Contrast Analyser
- 400% browser zoom — verify no content loss
- 320px CSS viewport — verify no horizontal scroll (except keyboard-accessible table regions)
- Windows High Contrast Mode (Edge) and macOS Increase Contrast

**Third-party audit vs. self-certification:** The EAA permits self-certification with documented evidence (test reports, axe exports, manual test records with screen reader + browser + tester + date). Self-certify initially with documented automated + manual testing. Commission a third-party WCAG audit (CPWA-certified auditor) before public launch to a broad user base. Budget approximately EUR 3,000–8,000 for a SPA of this complexity.

---

### Priority Remediation Checklist

#### Tier 1 — Legal prerequisites (must exist before any external user)

- [ ] Publish accessibility statement at `/accesibilidad` (conformance status, known gaps, contact, enforcement link)
- [ ] Set up `accesibilidad@[domain]` feedback inbox with ≤30-day SLA
- [ ] Add "Declaración de Accesibilidad" link to global footer
- [ ] Set `lang="es"` (or correct language) on `<html>` in both `_Layout.cshtml` and `index.html`

#### Tier 2 — High user impact (core flows for users with disabilities)

- [ ] Audit and fix all form label associations (no placeholder-as-label; `<Label htmlFor>` on every input)
- [ ] Error identification: all validation errors as text with `aria-describedby` association; focus moves to first error on failed submit
- [ ] Error suggestions: descriptive messages ("Use DD/MM/YYYY format", not "Invalid date")
- [ ] Focus visible on all interactive elements (audit for `focus:outline-none` without replacement)
- [ ] Dialog `<DialogTitle>` present on every `<Dialog>`; focus returns to trigger on close
- [ ] Toast severity: `aria-live="polite"` for success, `role="alert"` for errors
- [ ] Icon-only buttons: `aria-label` on all `<Button>` containing only a Lucide icon; `aria-hidden="true"` on the SVG
- [ ] DataTable: `aria-sort` on sorted columns, `<button>` inside sortable `<th>`, `aria-label` on row actions, `aria-live` result count, `<caption>` or `aria-label` on table

#### Tier 3 — Significant user impact (charts and complex widgets)

- [ ] Charts: `role="img"` + `aria-label` on every chart; data table alternative for each; keyboard-focusable data points; non-color differentiators for multi-series
- [ ] Create `ChartWrapper` component enforcing chart accessibility requirements
- [ ] Combobox: audit cmdk against ARIA APG combobox pattern; patch or replace if non-conformant; add result-count announcer
- [ ] Date picker: verify react-day-picker v8; test keyboard navigation; accessible trigger label
- [ ] Onboarding wizard: step heading focus on transition; `aria-live` step announcer; error focus management

#### Tier 4 — Contrast, reflow, text spacing

- [ ] Color contrast audit: run axe-core across all routes; fix `critical` contrast violations; verify chart labels
- [ ] Income/Expense and other status badges: text label must accompany color coding (not color-only)
- [ ] Reflow at 320px: DataTable within keyboard-accessible horizontal scroll container (`tabindex="0"`, `role="region"`, `aria-label`)
- [ ] Fixed-height containers: replace `h-[x]` with `min-h-[x]` on interactive rows/cards to survive text spacing overrides

#### Tier 5 — Tooling and process

- [ ] Add `eslint-plugin-jsx-a11y` to React ESLint config; treat violations as build failures
- [ ] Add `vitest-axe` assertions to tests for Dialog, DataTable, Combobox, DatePicker, Wizard
- [ ] Document accessibility testing protocol in `docs/testing.md` (screen reader combos, manual scripts, cadence)
- [ ] Commission third-party WCAG audit before public launch (budget EUR 3,000–8,000)

---

## Open Legal Questions

- [ ] Will the app eventually need to register as a data controller with AEPD? (Required in Spain once processing data of others at scale)
- [ ] Does the bank connectivity feature (Phase 4) require any licensing under PSD2 as an AISP (Account Information Service Provider)?
- [ ] What jurisdiction's law governs the Terms of Service if the user base becomes international?
- [ ] EU Accessibility Act — confirm exact private sector obligations and timeline under Spanish transposition law before Phase 3 launch. Requires qualified legal advice.
