# ADR-0069 — MFA Opt-In for Personal Users

**Status:** Accepted (Phase 3, Batch 3 — Auth)

**Date:** 2026-05-09

**Supersedes (in part):**

- The MFA bullet in `security-model.md` § Authentication and Session Rules → Login (TOTP required + 24-hour grace + cliff lockout).
- The MFA bullet in `planning-phase3.md` § Planned Features (mandatory TOTP for all users).
- The "TOTP MFA mandatory for all users" clause in `planning-resolved.md`'s Authentication-framework entry.
- The "TOTP enrolment is mandatory; 24-hour grace period for first login; cannot be skipped" verification item in `roadmap-phase-three.md` § Stage 6 → TOTP block.
- The inline "MFA via TOTP authenticator app (no SMS) is mandatory alongside password-based login" line in ADR-0001 (the surrounding decision — defer auth to Phase 3 — stays accepted).

The original mandate was never an ADR; it lived as a policy bullet in `security-model.md`. This ADR is the first formal decision on MFA enforcement.

## Context

Phase 3 ships authentication for the first time. The pre-2026-05-09 plan, drafted before the implementation work began, mandated TOTP MFA for every user, with a 24-hour post-registration grace period after which login itself was blocked until enrollment completed. The Stage 6b.1 brainstorming session re-examined this decision when the implementation surface forced a concrete user-facing flow to be specified.

The case for re-examining was driven by three factors:

1. **No comparable product mandates MFA.** Industry research (see § Evidence below) found that every major personal-finance product Project Ceres competes with — Monarch Money, YNAB, Empower (formerly Personal Capital), the now-defunct Mint — treats MFA as opt-in via Settings → Security. Even pure security products (1Password, Bitwarden, LastPass) treat MFA as opt-in for personal accounts. The single industry tightening shipped in 2025 is Bitwarden's email-link step-up for non-MFA users on new devices — a soft compensating control, not an enrollment mandate.

2. **Onboarding-conversion impact would be severe.** The fintech baseline drop-off rate is 63% (Eleken). 68% of consumers abandon mid-onboarding when the process feels too long (Jumio). Mandatory TOTP at registration adds at least four stacked friction points: install authenticator app, scan QR, type 6-digit code correctly, save 10 backup codes — all before the user has seen any value from the product. Each friction point compounds; the effect on a beta-stage product without name recognition would likely push drop-off well past 80%.

3. **The mandate exceeds the security level NIST calls for at this risk profile.** NIST SP 800-63B-4 classifies authentication into three Authenticator Assurance Levels. AAL1 (single-factor authentication) is the level that fits Project Ceres: a personal-finance tracker where users connect to their own data, no money moves through the system, no regulated transactions occur. AAL1 mandates breach-screened passwords + slow hashing + rate limiting + session management + auth-event notification — none of which require TOTP. TOTP only becomes a regulator-mandated requirement at AAL2, which applies to systems handling regulated data (federal benefits, healthcare records, certain bank-side transactions). Project Ceres is not such a system.

The original mandate was driven by a "safer default at beta scale" argument plus a one-off security-model.md bullet that was never re-examined against industry practice or the actual NIST classification. Both supports collapse on inspection.

## Evidence

### Industry-norm survey (May 2026)

| Product | Category | MFA policy |
|---|---|---|
| Monarch Money | Personal finance (closest direct comparable) | **Opt-in** via Settings → Security ([Monarch help](https://help.monarch.com/hc/en-us/articles/360054392152-Multi-Factor-Authentication)) |
| YNAB | Personal finance | **Opt-in** via Account Settings ([YNAB blog](https://www.ynab.com/blog/added-security-for-ynab-with-two-step-verification)) |
| Empower (formerly Personal Capital) | Personal finance + investments | Opt-in |
| Mint (until 2024 shutdown) | Personal finance | Was opt-in |
| 1Password | Password manager (security product) | **Opt-in** ([1Password support](https://support.1password.com/two-factor-authentication/)) |
| Bitwarden | Password manager (security product) | **Opt-in.** March 2025: added email-link step-up for non-MFA users logging in from a new device. The step-up is mandatory for affected users; MFA enrollment itself remains opt-in. |
| LastPass | Password manager | Opt-in |

Banks issue MFA — but it is typically SMS or email-OTP, opted into per session ("send me a code"), not authenticator-app TOTP enforced at registration. The class of products that *do* mandate TOTP is enterprise SaaS where an organisation admin enforces it on the org's behalf; that is a fundamentally different relationship to the user.

### Onboarding-conversion data

| Source | Finding |
|---|---|
| [Eleken — Fintech onboarding](https://www.eleken.co/blog-posts/fintech-onboarding-simplification) | Average onboarding drop-off rate in fintech is 63%. |
| [Jumio — Customer abandonment](https://www.jumio.com/how-to-reduce-customer-abandonment/) | 68% of consumers have abandoned digital banking applications mid-onboarding. |
| [Phenomenon Studio — Cost of slow onboarding](https://phenomenonstudio.com/article/the-cost-of-slow-onboarding-how-ux-drives-fintech-growth/) | 73% of users abandon financial apps during onboarding due to bad design ($18B/year industry cost). |
| [Mojoauth — Login friction white paper](https://mojoauth.com/white-papers/login-friction-user-experience-problems/) | 56% of US consumers have abandoned a registration because the process felt tedious. |
| [arXiv:2210.09373 — 2FA consistency study](https://arxiv.org/pdf/2210.09373) | Inconsistent 2FA implementations cause friction that "led users to refuse 2FA or abandon websites" — even at the prompt-to-enable point, not at the harder mandatory-at-registration point. |

The arXiv paper is the most directly relevant: it studied users *who already have an account* being asked to enable 2FA on top-ranked websites, and found that even there friction caused abandonment. Mandatory 2FA *at registration*, before the user has any sunk cost in the account, would necessarily produce higher abandonment.

### NIST AAL classification

Per NIST SP 800-63B-4 § Authenticator Assurance Levels, Project Ceres at Phase 3 is AAL1:

- **AAL1** — single-factor authentication is sufficient. Required controls: memorized-secret screening against breach blocklists, rate limiting, session management, auth-event notification.
- **AAL2** — multi-factor required. Applies to "personal information... where the consequence of unauthorized release is significant" — typically interpreted as regulated data (federal benefits, healthcare under HIPAA, regulated banking transactions).
- **AAL3** — hardware-based MFA required. Applies to systems where compromise causes "serious or catastrophic" harm.

Project Ceres holds personal financial *records* but no regulated transactions, no money movement, no integration with banking rails. The data is sensitive (financial history is private) but the unauthorized-release consequence is recovery-of-data-integrity, not regulator-defined harm. AAL1 is the correct classification, and AAL1's required controls do not include MFA.

## Decision

MFA via TOTP is offered as an **opt-in security feature** in Project Ceres.

Specifically:

1. **Login does not block on MFA enrollment.** Valid credentials → session issued. If the user has previously enabled MFA, the TOTP step runs as a second phase; if they have not, login completes immediately. There is no grace period and no enforcement deadline.
2. **Onboarding (Stage 9 / Stage 15.5) presents MFA as a recommended-but-skippable step.** The wizard step explains the security benefit, offers a "Set it up now" path, and offers a "Skip — I'll do this later" path. No countdown timers, no nags on subsequent logins, no banners blocking the dashboard.
3. **Settings → Security exposes MFA as an opt-in toggle.** Clear copy: "Add a second verification step at login. Recommended if you sign in from multiple devices or use shared networks." Backup codes are generated on enable and shown once.
4. **Sensitive operations require step-up authentication.** Password change, email change, GDPR erasure, account deletion: the user must complete a fresh authentication. For users with MFA enabled, the step-up requires a fresh TOTP code. For users without MFA, the step-up requires a fresh password (the email-link channel ownership proof is the second factor). This is a Stage 6c concern — Stage 6b.1 ships only the TOTP infrastructure that step-up depends on.
5. **New-device login from non-MFA accounts triggers email-link step-up** (Bitwarden-style). When a request authenticates via password from an IP/UA combination that has never been used before for this user, an email-link verification is required to complete the login. This catches the credential-stuffing attack vector — an attacker with a leaked password but no email access cannot complete login. Deferred to Stage 6c (depends on the email service shipping in Stage 8).
6. **No SMS-based 2FA, ever.** SIM-swap vulnerability is well-documented. Already covered by `security-model.md`.

The compensating-control stack that makes opt-in MFA acceptable for non-MFA accounts:

| Layer | What it blocks | Stage |
|---|---|---|
| HIBP breached-password screening at register/change-password | Credential stuffing using known-breached passwords (the dominant attack vector). | 6a (shipped) |
| Argon2id with OWASP params (m=19456, t=2, p=1) | Offline brute force from a stolen DB dump. | 6a (shipped) |
| Account lockout after N failed attempts + per-IP rate limit | Online brute force / credential spraying. | 6b.2 |
| Failed-login logging | Cross-account credential-stuffing detection patterns. | 6b.2 |
| Self-service unlock signed-token | Recovers legitimate users from lockout without admin intervention. | 6b.2 |
| New-device email-link step-up (Bitwarden pattern) | Credential-stuffing success: attacker has password but not email access. | 6c |
| Step-up reauthentication on sensitive operations | Account takeover causing actual damage (password change, email change, data export, deletion). | 6c |
| New-device / new-IP login email alert with revoke link | Successful ATO: legitimate user is notified within seconds and can revoke the session. | 6c |
| Session revocation on logout / session expiry / password change | Session reuse after credential change. | 6a (shipped) |
| CSRF double-submit on state-changing endpoints | Cross-site request forgery from an authenticated session. | 6a (shipped) |
| EF global query filters + service-layer redundancy + RLS | Cross-tenant data leakage even if auth is bypassed at any layer above. | 6a + 7 + 7.5 |

The stack matches NIST AAL1's required controls and exceeds them on several axes (Argon2id parameters above OWASP minimum; layered IDOR prevention; Bitwarden-style step-up).

## Consequences

**Positive:**
- Onboarding drop-off measured against the fintech baseline (63%), not the fintech baseline + mandatory-MFA penalty.
- Industry-aligned UX. New users from Monarch / YNAB / Mint find the auth experience familiar.
- Security users can opt in immediately; non-security users are not gatekept out of their own product.
- Compensating controls are real defences, not bureaucratic paperwork. HIBP screening + new-device email step-up + step-up on sensitive ops collectively close the credential-stuffing → ATO → damage path.
- The Bitwarden step-up pattern lands cleanly in 6c when the email service ships; no retrofit needed in 6b.1's login pipeline.

**Negative:**
- A user who chose not to enable MFA, and whose email account is also compromised, has an account-takeover surface that an MFA-mandatory system would not have. This is acknowledged and accepted: the user has chosen the trade-off; the email channel ownership is the implicit second factor; account-takeover under the layered controls requires multiple simultaneous compromises.
- "MFA available" is a weaker marketing claim than "MFA required." Acceptable — Project Ceres is not selling on "we forced you to enable security."
- ADR-0001 + four other docs require sync edits per the supersession-sweep rule. Done in the same commit as this ADR.

**Implementation notes:**
- Stage 6b.1 still ships the full TOTP infrastructure (enrollment, backup codes, replay prevention, login flow integration). The infrastructure is what users *who choose to enable* MFA depend on.
- `ApplicationUser.CreatedAt` is still added in Stage 6b.1 — it has no behavioural role in the login flow but is useful for audit and analytics.
- The security-model.md "MFA mandatory" line and the planning-phase3.md "MFA mandatory" line are amended in the same commit as this ADR. The supersession-sweep rule (`feedback_persist_deferred_decisions.md`) requires every doc that referenced the original decision be updated.

## Cross-references

- ADR-0001 — Authentication deferred to Phase 3. Inline mandatory-MFA clause superseded by this ADR.
- ADR-0019 — Session management; user-configurable lifetime + IP controls. Unchanged.
- ADR-0063 — Cookie SameSite=Lax with CSRF tokens. Unchanged.
- ADR-0064 — Social login deferred to Phase 4. Unchanged.
- `security-model.md` § Authentication and Session Rules → Login → MFA bullet — amended in the same commit.
- `security-model.md` § Password Reset → MFA during reset — amended in the same commit.
- `planning-phase3.md` § Authentication + § MFA bullets — amended in the same commit.
- `planning-resolved.md` Authentication-framework entry — amended in the same commit.
- `roadmap-phase-three.md` Stage 6 verification checklist (TOTP block) — amended in the same commit.

## Sources

- NIST SP 800-63B-4 (final 2025-07-31): https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-63B-4.pdf
- NIST SP 800-63B-4 § Authenticators: https://pages.nist.gov/800-63-4/sp800-63b/authenticators/
- Multi-Factor Authentication — Monarch Money help: https://help.monarch.com/hc/en-us/articles/360054392152-Multi-Factor-Authentication
- Added Security for YNAB with Two-Step Verification: https://www.ynab.com/blog/added-security-for-ynab-with-two-step-verification
- Turn on two-factor authentication for your 1Password account: https://support.1password.com/two-factor-authentication/
- Eleken — Fintech onboarding: 6 UX practices that reduce drop-off: https://www.eleken.co/blog-posts/fintech-onboarding-simplification
- Jumio — How to Reduce Customer Onboarding Abandonment: https://www.jumio.com/how-to-reduce-customer-abandonment/
- Phenomenon Studio — The cost of slow onboarding: https://phenomenonstudio.com/article/the-cost-of-slow-onboarding-how-ux-drives-fintech-growth/
- Mojoauth — Login Friction and User Experience Problems: https://mojoauth.com/white-papers/login-friction-user-experience-problems/
- Ghorbani Lyastani et al. — A Systematic Study of the Consistency of Two-Factor Authentication User Journeys (arXiv:2210.09373): https://arxiv.org/pdf/2210.09373
- Ping Identity — Risk-Based Authentication: https://www.pingidentity.com/en/resources/identity-fundamentals/authentication/risk-based-authentication.html
- Microsoft Entra ID Protection — Risk-based user sign-in protection: https://learn.microsoft.com/en-us/entra/identity/authentication/tutorial-risk-based-sspr-mfa
