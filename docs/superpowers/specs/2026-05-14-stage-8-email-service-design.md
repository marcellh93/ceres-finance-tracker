# Stage 8 — Email Service + Email Security (Design)

**Date:** 2026-05-14
**Phase:** 3 (Hosted Beta) — Batch 3d
**Status:** Draft, pending user review

---

## Scope

Stage 8 makes outgoing transactional email real:

- A production `IEmailService` implementation backed by **Resend** (provider chosen 2026-05-12 per `planning-resolved.md` § Email provider for Phase 3).
- The application-layer protections from `security-model.md` § Email Security Rules: recipient lock, sanitization, per-user + per-IP rate limiting, send-only API key in the secrets store.
- An `IEmailComposer` + `IStringLocalizer<EmailsResource>` template path so every email body lives in EN + ES `.resx` files, not in C# string concatenation inside service classes.
- A webhook endpoint that ingests Resend delivery events (delivered / bounced / complained), records them in `EmailDeliveryEvent`, and flips `ApplicationUser.EmailConfirmed = false` on bounce.
- The SPF / DKIM / DMARC runbook published as `docs/runbooks/email-dns-setup.md`. **Publication of the DNS records themselves is deferred to Stage 16** (no domain registered yet); the runbook is the deliverable from this stage.

**Out of scope** — call-sites for emails that don't fire yet (registration-confirmation, TOTP-enrolled / disabled, backup-codes-regenerated, new-session alert, GDPR-export-ready, account-erasure-confirmation). EmailsResource keys for these are **not reserved** in this stage — they'll be added with the stage that wires the call-site (9 / 10 / 13). Adding placeholder keys here would create translation churn for templates whose copy doesn't exist.

**Brand naming** — every user-facing email uses **"Ceres"**, never "Project Ceres". "Project Ceres" is the internal codename; it does not appear in any resx value.

---

## Architecture

Four collaborators in `ProjectCeres.Common.Email`, each with one job:

### `IEmailService` (existing, kept)

Single method `Task SendAsync(EmailMessage message, CancellationToken ct)`. Two implementations registered conditionally in `Program.cs`:

- `LogOnlyEmailService` (existing, kept) — dev default when `Email:Resend:ApiKey` is unbound. Writes the message at `Information` level.
- `ResendEmailService` (new) — registered when `Email:Resend:ApiKey` is bound. Adapts the internal `EmailMessage` record into the Resend SDK's request shape and calls `IResend.Emails.SendAsync` with retry.

Production behavior: if `Email:Resend:ApiKey` is unbound at startup AND `Environment == "Production"`, `Program.cs` throws `InvalidOperationException` immediately. No silent fallback to `LogOnlyEmailService` in production. The current line `// Production deliberately has no IEmailService implementation registered.` (Program.cs:153) upgrades to a loud throw.

### `IEmailComposer` (new)

```csharp
public enum EmailTemplateKey
{
    PasswordResetRequest,
    PasswordChanged,
    PasswordResetCancelledEmailChange,         // sent when /password-reset/confirm cancels a pending email-change
    EmailChangeVerifyNew,                      // sent to new address
    EmailChangeRevokeOld,                      // sent to old address with revoke link
    EmailChangeConfirmed,                      // sent to new address after /confirm
    EmailChangeConfirmedToOld,                 // sent to old address after /confirm
    EmailChangeRevokeNotificationToOld,        // sent to old address after /revoke
    LockoutUnlock,
}

public interface IEmailComposer
{
    EmailMessage Compose(EmailTemplateKey key, CultureInfo culture, params object[] args);
}
```

Implementation reads three resx strings via `IStringLocalizer<EmailsResource>` under the supplied culture (`{key}.Subject`, `{key}.BodyText`, `{key}.BodyHtml`), sanitizes every arg per slot type (HTML-encode for HTML body, strip CR/LF for subject), runs `String.Format(culture, template, args)`, returns `new EmailMessage(EmailRecipient.None, subject, html, text)` — the `To:` is set by the caller after recipient resolution.

### `IEmailRecipientResolver` (new)

```csharp
public sealed class EmailRecipient
{
    public string Address { get; }
    private EmailRecipient(string address) => Address = address;

    internal static readonly EmailRecipient None = new("");

    internal static EmailRecipient FromVerifiedUser(string email) => new(email);

    /// <summary>
    /// Email-change flow only. The user's CURRENT row in ApplicationUser.Email is
    /// the new address; the email-change services need to send a notification to
    /// the OLD address. The old address is read server-side from EmailChangeToken,
    /// never from a request payload. Only EmailChangeService is allowed to call this.
    /// </summary>
    internal static EmailRecipient OverrideForEmailChange(string oldEmail) => new(oldEmail);
}

public interface IEmailRecipientResolver
{
    Task<EmailRecipient> ResolveAsync(Guid userId, CancellationToken ct);
}
```

`ResolveAsync` reads `ApplicationUser.Email` for the given `userId`. Throws `EmailRecipientNotResolvableException` if the user doesn't exist or has no verified email. `userId` always originates from session / token / audit context — never from a request parameter.

**Compile-time enforcement of the security-model § Layer 2 recipient-lock rule.** `EmailMessage`'s `To` slot changes type from `string` to `EmailRecipient`. There is no public `string → EmailMessage` constructor anywhere; a service cannot construct an `EmailMessage` with a caller-supplied raw string. The only way to populate `To` is via the resolver or the email-change override factory. A unit test asserts `EmailMessage` has no public constructor accepting a raw `string` in the To slot.

**Greppable audit of the override.** `EmailRecipient.OverrideForEmailChange` is `internal` to `ProjectCeres`. The codebase has exactly one legitimate caller (`EmailChangeService`). A unit test reflects over the `ProjectCeres` assembly and asserts only `ProjectCeres.Common.Authentication.EmailChangeService` references `OverrideForEmailChange`. No `[PreAuthCallSite]` attribute — that attribute applies to controller action methods per ADR-0073, not to factory methods. The greppable-name + reflection-test pattern replaces an attribute here.

### `ILanguageResolver` (new)

```csharp
public interface ILanguageResolver
{
    Task<CultureInfo> ResolveForUserAsync(Guid userId, CancellationToken ct);
}
```

Reads `Settings.Language` for the given user, returns the matching `CultureInfo`. Fallback to `en` if no row, no language set, or the value isn't one of `en` / `es`.

**Schema dependency:** `Settings.Language` does not exist on the `Settings` entity today (verified 2026-05-14). Stage 8 adds it. Single-column migration in sub-stage 8b:

```csharp
public class Settings : IUserOwned
{
    // existing fields unchanged
    public string Language { get; set; } = "en";  // NEW: "en" | "es"
}
```

The Stage 9 SPA work will wire the user-facing language toggle that PATCHes this column. Stage 8 only adds the column with default `"en"`, so existing rows backfill to English.

### `ResendWebhookController` (new)

Route `POST /api/internal/email-webhook/resend`. `[AllowAnonymous]` + `[IgnoreAntiforgeryToken]`. Verifies the Svix signature on the raw request body (Resend uses Svix for webhooks):

1. Extracts `Svix-Id`, `Svix-Timestamp`, `Svix-Signature` headers.
2. Computes `HMAC-SHA256(WebhookSecret, $"{svixId}.{svixTimestamp}.{rawBody}")`.
3. Constant-time compare against the signature claim via `CryptographicOperations.FixedTimeEquals`.
4. Timestamp tolerance: ±5 minutes.

On valid signature: deserialize the event, write a row to `EmailDeliveryEvent`, and apply side effects:

- `email.bounced` → `UPDATE AspNetUsers SET EmailConfirmed = false WHERE NormalizedEmail = @addr`. Record the bounce reason in the event payload.
- `email.complained` → record event only. The digest-opt-in column doesn't exist yet (digest is Phase 4); the wiring point exists for later.
- `email.delivered` / `email.sent` → record event, no side effect.

Returns `204 No Content` on accepted events, `401 Unauthorized` on bad signature, `400 Bad Request` on malformed body. (Webhook errors don't go through `InvalidModelStateResponseFactory`; signature failure is an auth failure, not a validation failure.)

Rate-limited by `EmailByIp` (10/hour/IP) — Resend retries on failure but at low volume; this is a backstop against an attacker hammering the unauthenticated endpoint.

---

## Resx layout

```
ProjectCeres/Resources/
  EmailsResource.cs       ← one-line marker type
  Emails.en.resx          ← 27 keys (9 templates × Subject/BodyText/BodyHtml)
  Emails.es.resx          ← same 27 keys, Spanish translations
```

`EmailsResource.cs`:

```csharp
namespace ProjectCeres.Resources;

/// <summary>Marker type for IStringLocalizer&lt;EmailsResource&gt;.</summary>
public sealed class EmailsResource { }
```

Key set — one row per `EmailTemplateKey` value, three keys each (`.Subject`, `.BodyText`, `.BodyHtml`):

| Template | Placeholders | Sent to |
|---|---|---|
| `PasswordResetRequest` | `{0}` = reset URL | user's current verified address |
| `PasswordChanged` | — | user's current verified address |
| `PasswordResetCancelledEmailChange` | — | user's current verified address (pending email-change is cancelled) |
| `EmailChangeVerifyNew` | `{0}` = verify URL | new address (`OverrideForEmailChange`-style; from `EmailChangeToken.NewEmail`) |
| `EmailChangeRevokeOld` | `{0}` = new email address, `{1}` = revoke URL | old address (`OverrideForEmailChange`) |
| `EmailChangeConfirmed` | — | new address (now the current `user.Email`) |
| `EmailChangeConfirmedToOld` | `{0}` = old email, `{1}` = new email | old address (`OverrideForEmailChange`) |
| `EmailChangeRevokeNotificationToOld` | — | old address (`OverrideForEmailChange`) |
| `LockoutUnlock` | `{0}` = unlock URL, `{1}` = IP address | user's current verified address |

EN sample (`Emails.en.resx`):

- `PasswordResetRequest.Subject` = `Reset your Ceres password`
- `LockoutUnlock.Subject` = `Ceres account locked`
- `EmailChangeConfirmed.Subject` = `Ceres email change confirmed`

ES sample (`Emails.es.resx`):

- `PasswordResetRequest.Subject` = `Restablece tu contraseña de Ceres`
- `LockoutUnlock.Subject` = `Cuenta de Ceres bloqueada`
- `EmailChangeConfirmed.Subject` = `Cambio de correo electrónico confirmado en Ceres`

A unit test `EmailsResource_AllKeysPresentInBothCultures` enumerates `Emails.en.resx`'s keys and asserts each exists in `Emails.es.resx` (and vice versa). Catches a translator dropping a key on a future edit.

---

## Resend wiring

### NuGet

```xml
<PackageReference Include="Resend" Version="0.1.5" />  <!-- latest stable at time of writing -->
```

### Configuration

```csharp
public sealed class EmailOptions
{
    public ResendOptions Resend { get; init; } = new();
}

public sealed class ResendOptions
{
    public string? ApiKey { get; init; }
    public string FromAddress { get; init; } = "onboarding@resend.dev";
    public string FromName { get; init; } = "Ceres";
    public string? WebhookSecret { get; init; }
}
```

Bound in `Program.cs`:

```csharp
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
```

**Secret storage:**

- Dev: `dotnet user-secrets set "Email:Resend:ApiKey" "re_..."` (the project already has a `UserSecretsId` configured — verified in `ProjectCeres.csproj`).
- Prod: `Email__Resend__ApiKey` environment variable.

**Conditional registration in `Program.cs`:**

```csharp
var resendKey = builder.Configuration["Email:Resend:ApiKey"];
if (string.IsNullOrWhiteSpace(resendKey))
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException(
            "Email:Resend:ApiKey is required in Production. " +
            "Set the Email__Resend__ApiKey environment variable.");

    builder.Services.AddSingleton<IEmailService, LogOnlyEmailService>();
}
else
{
    builder.Services.AddHttpClient<IResend, ResendClient>(c =>
        c.DefaultRequestHeaders.Authorization = new("Bearer", resendKey));
    builder.Services.AddSingleton<IEmailService, ResendEmailService>();
}
```

### Retry policy

`ResendEmailService` wraps `_resend.Emails.SendAsync` in 3-attempt exponential backoff: 250ms → 1s → 4s.

- **Retry**: `HttpRequestException` with status `5xx`; status `429`.
- **No retry**: `400` (bad request), `401` (bad key), `403` (forbidden), `422` (unprocessable — unverified domain falls here). Log at `Error`, rethrow.

Callers (`PasswordResetService`, `EmailChangeService`, `LockoutUnlockService`) already wrap `SendAsync` in `try`/`catch` and log without blocking the HTTP response. That pattern stays; Stage 8 doesn't change it.

### `From:` address

Stage 8 ships with `FromAddress = "onboarding@resend.dev"` as the dev/test default. The operator overrides via configuration once a domain is verified in Resend.

---

## Rate limiting

Two new policies registered alongside `AuthLoginByIp` and `AuthMfaByUser` in `Program.cs`:

```csharp
public const string EmailByUser = "email-by-user";  // 5 / 60 min / user
public const string EmailByIp   = "email-by-ip";    // 10 / 60 min / IP
```

Both use `SlidingWindowRateLimiter`, matching the existing auth policy shape.

### Endpoint attachment

| Endpoint | Existing policy (unchanged) | New policy added |
|---|---|---|
| `POST /api/auth/password-reset/request` | `AuthLoginByIp` | `EmailByUser` + `EmailByIp` |
| `POST /api/auth/email-change/request` | `AuthReauthByUser` | `EmailByUser` |
| `POST /api/auth/lockout/unlock/request` | `AuthLoginByIp` | `EmailByUser` + `EmailByIp` |
| `POST /api/internal/email-webhook/resend` | — | `EmailByIp` |

ASP.NET requires composition via a single policy rather than chaining two attributes. The new policies internally compose the per-IP fallback in their `GetPartition` so two attributes aren't needed at the endpoint.

### Timing-channel preservation (Stage 6.16 follow-up)

Stage 6.16 fixed an Argon2id-call-count timing channel where `/password-reset/request` and `/email-change/request` had different work counts across happy / unknown-user / duplicate-email branches. A naïve rate-limit-by-`UserId` would re-introduce the channel: if `UserId` is unknown (anonymous request), the limiter short-circuits *before* the service runs its constant-time dummy hashes, making "user exists" observable as "took longer to 429".

Fix: `EmailByUser` for `/password-reset/request` and `/lockout/unlock/request` partitions by the **normalized email string from the request payload**, not by `UserId`. Known and unknown emails take the same code path through the limiter.

For `/email-change/request` (auth-gated), partition is `UserId` — only authenticated users reach this endpoint, so the timing-channel concern doesn't apply.

The Stage 6.16 `Argon2idCallCounter` regression tests stay green. A new test (`EmailByUserRateLimit_429s_Across_Branches_Have_Equal_Argon2id_Call_Count`) pins that the `EmailByUser` 429 path doesn't reintroduce the timing channel.

---

## Sanitization

`EmailComposer` enforces three rules on every `args[i]` before `String.Format`:

1. **HTML body slots**: `HtmlEncoder.Default.Encode(arg.ToString())`. Resx templates contain literal HTML; only user-supplied values are encoded. URLs are NOT pre-encoded by the composer — auth services construct URLs via `LinkGenerator` so they're well-formed.
2. **Subject lines**: after `String.Format`, run `.Replace("\r", "").Replace("\n", "")`. Defense-in-depth against header injection if a sanitized arg ever ends up adjacent to the subject; no current template puts user input in a subject.
3. **Text body slots**: same CR/LF strip as subject for arguments that might end up adjacent to a header-shaped construct.

The six existing emails interpolate only URLs (server-constructed) and IP addresses (read from `HttpContext.Connection.RemoteIpAddress`). Sanitization is defense-in-depth in Stage 8, not a fix for a current vulnerability. Pinned by `EmailComposer_RejectsCrLfInArgs_Subject` and `EmailComposer_HtmlEncodesArgsInHtmlBody`.

---

## `EmailDeliveryEvent` entity

```csharp
public sealed class EmailDeliveryEvent : IUserOwned
{
    public Guid Id { get; init; }
    public Guid? UserId { get; init; }              // nullable: bounces from unknown addresses still recorded
    public string MessageId { get; init; } = "";    // Resend's id
    public string Type { get; init; } = "";         // email.sent | email.delivered | email.bounced | email.complained
    public string EmailAddress { get; init; } = "";
    public string Payload { get; init; } = "";      // raw JSON, jsonb in PG
    public DateTimeOffset OccurredAt { get; init; }
}
```

Single EF migration adds the table plus an index on `(EmailAddress, OccurredAt DESC)`.

`IUserOwned` lets it participate in the Stage 7.5 RLS scheme. The webhook path runs cross-tenant (no `UserContext`); `ResendWebhookController` carries the same `IgnoreQueryFilters` + Stage 10 architecture-test allow-list comment that every other cross-tenant pre-auth path uses.

---

## DNS runbook — `docs/runbooks/email-dns-setup.md`

Ships in Stage 8 (sub-stage 8f). Documents the exact records to publish when the sending domain is registered. Contents:

1. Verify a sending domain in Resend → copy the DKIM CNAMEs Resend supplies into the domain's DNS zone.
2. Publish SPF: `v=spf1 include:_spf.resend.com -all`.
3. Publish DMARC at `p=none` with `rua=mailto:dmarc@<domain>`:
   `v=DMARC1; p=none; rua=mailto:dmarc@<domain>; aspf=s; adkim=s`.
4. Wait 30 days, verify aggregate reports show only Resend-signed mail.
5. Advance to `p=quarantine` then `p=reject` via `pct=` ramp (25 → 50 → 100, 90 days each per `security-model.md` § Layer 1).
6. Publish MTA-STS + TLS-RPT records (per the security-model table).
7. Rotation procedure: dual-selector DKIM rotation (publish new selector, switch sending, wait 48h for TTL, remove old). Semi-annual minimum per `security-model.md`.

**DNS records themselves are NOT published in Stage 8** — no domain registered yet. The runbook is the deliverable. Stage 16 (hosting + ops) executes the runbook against the chosen domain.

Roadmap close-out treatment:

- Items provable by automated test → `[x]` at Stage 8 close.
- Items that ship as runbook but need DNS publication → `[~]` with a "pending Stage 16" link.
- Items for unsent emails (registration / TOTP / etc.) → `[ ]` with a "deferred to Stage N" link.

---

## Sub-stage sequencing

Six independently committable sub-stages. The suite stays green at each step.

| Sub-stage | What ships | Tests required | Doc-sync |
|---|---|---|---|
| **8a** | `EmailRecipient` value type; `EmailMessage.To` changes from `string` to `EmailRecipient`; `IEmailRecipientResolver`; auth services migrated to use the resolver (still using `LogOnlyEmailService`, English-only — every existing `Build*Email` helper still constructs subject/body inline, just routes the To through the resolver) | Compile + every existing email test still green; `EmailMessage_NoStringToConstructorExists` (reflection); `EmailRecipientOverride_OnlyCalledByEmailChangeService` (reflection over assembly types) | None |
| **8b** | `Settings.Language` column added (default `"en"`, single EF migration); `EmailsResource` + `Emails.en.resx` + `Emails.es.resx` (27 keys, nine templates); `IEmailComposer` + `EmailComposer` impl; `ILanguageResolver` + `LanguageResolver` impl; every `Build*Email` helper in `PasswordResetService` / `EmailChangeService` / `LockoutUnlockService` deleted (3 + 5 + 1 = 9 helpers); services call the composer | `EmailComposer_RendersAllNineTemplates_EnAndEs`; `EmailsResource_AllKeysPresentInBothCultures`; `EmailComposer_HtmlEncodesArgsInHtmlBody`; `EmailComposer_StripsCrLfInSubject`; `LanguageResolver_FallsBackToEn_WhenLanguageColumnUnset`; `LockoutUnlock_ResolvesCultureFromUserSettingsLanguage` | `planning-phase3.md § Localization` — mark server-side email plumbing ✅ shipped |
| **8c** | `Resend` NuGet package added; `EmailOptions` + `ResendOptions`; `ResendEmailService`; conditional `Program.cs` registration; production-throws-without-key | `ResendEmailService_RetriesOnTransient_NoRetryOn4xx` (mocked HTTP); `ResendEmailService_FailsLoudInProductionWithoutApiKey` (WebApplicationFactory env=Production); manual smoke test sending to your own inbox via `onboarding@resend.dev` | `security-model.md § Layer 3` — cross-link to dev/prod secret-source convention |
| **8d** | `EmailByUser` + `EmailByIp` rate-limit policies; attached to `/password-reset/request`, `/email-change/request`, `/lockout/unlock/request`, `/api/internal/email-webhook/resend` | `EmailByUserRateLimit_429sAfter5thSend`; `EmailByIpRateLimit_429sAfter10thSend`; `EmailByUserRateLimit_429s_Across_Branches_Have_Equal_Argon2id_Call_Count` (Stage 6.16-style timing-channel preservation); `EmailChangeRequest_PartitionsByUserId` | `security-model.md § Layer 2` — cross-link |
| **8e** | `EmailDeliveryEvent` entity + migration; `ResendWebhookController`; `ResendSignatureVerifier`; bounce-flips-`EmailConfirmed` wiring | `ResendWebhook_RejectsBadSignature`; `ResendWebhook_AcceptsValidSignature_RecordsEvent`; `ResendWebhook_BouncedEvent_FlipsEmailConfirmed`; `ResendWebhook_TimestampOutsideTolerance_Rejects` | `models.md` adds `EmailDeliveryEvent` row |
| **8f** | `docs/runbooks/email-dns-setup.md`; roadmap 8.3 marked `[~]` with link; `roadmap-phase-three.md § Stage 16` gets new `[ ]` line "Publish SPF/DKIM/DMARC records per runbook"; `planning-phase3.md § Phase 3 Roadmap` row 3d marked ✅ shipped for 8a–8e and runbook-deferred for 8.3 | Runbook self-consistency review | Runbook ships; `security-model.md § Email Security Rules` cross-links to runbook |

Each sub-stage commits cleanly. None block on the next.

---

## File touch list

### New files

```
ProjectCeres/
  Common/Email/
    EmailComposer.cs                  ← IEmailComposer + impl
    EmailRecipient.cs                 ← value type w/ factories
    EmailRecipientResolver.cs         ← IEmailRecipientResolver + impl
    EmailTemplateKey.cs               ← enum
    EmailOptions.cs                   ← config record
    LanguageResolver.cs               ← ILanguageResolver + impl
    ResendEmailService.cs             ← IEmailService impl
    ResendSignatureVerifier.cs        ← Svix HMAC verification
    EmailDeliveryEvent.cs             ← new entity
  Resources/
    EmailsResource.cs                 ← marker type
    Emails.en.resx                    ← 18 keys
    Emails.es.resx                    ← same 18 keys, Spanish
  Controllers/
    ResendWebhookController.cs        ← POST /api/internal/email-webhook/resend

ProjectCeres/Migrations/
  YYYYMMDDHHMMSS_AddSettingsLanguageColumn.cs        ← 8b
  YYYYMMDDHHMMSS_AddEmailDeliveryEvent.cs            ← 8e

docs/runbooks/
  email-dns-setup.md                  ← 8f
```

### Modified files

```
ProjectCeres/
  ProjectCeres.csproj                 ← add Resend NuGet
  Program.cs                          ← EmailOptions binding, conditional IEmailService registration, EmailByUser/EmailByIp policies, IEmailComposer/IEmailRecipientResolver/ILanguageResolver DI
  Common/Email/EmailMessage.cs        ← To becomes EmailRecipient, not string
  Common/Email/IEmailService.cs       ← doc comment update only (signature unchanged)
  Common/Authentication/PasswordResetService.cs   ← replace BuildRequestEmail / BuildEmailChangeCancelledByPasswordResetEmail / BuildChangedEmail with composer calls (3 helpers deleted)
  Common/Authentication/EmailChangeService.cs     ← replace BuildVerifyNewEmail / BuildRevokeOldEmail / BuildChangeConfirmedEmail / BuildChangeConfirmedToOldEmail / BuildRevokeNotificationToOldEmail with composer calls (5 helpers deleted)
  Common/Authentication/LockoutUnlockService.cs   ← replace BuildLockoutEmail with composer call (1 helper deleted)
  Common/Authentication/AuthRateLimitPolicies.cs  ← add EmailByUser, EmailByIp constants
  Models/Settings.cs                  ← add Language column
```

---

## Tests required for stage close-out

Ship-gate (per `feedback_test_edge_cases_as_ship_gate`):

- `EmailComposer_RendersAllNineTemplates_EnAndEs`
- `EmailsResource_AllKeysPresentInBothCultures`
- `EmailComposer_HtmlEncodesArgsInHtmlBody`
- `EmailComposer_StripsCrLfInSubject`
- `EmailMessage_NoStringToConstructorExists` (reflection — pins the compile-time recipient lock)
- `EmailRecipientOverride_OnlyCalledByEmailChangeService` (reflection — pins the override-factory audit)
- `LanguageResolver_FallsBackToEn_WhenLanguageColumnUnset`
- `LockoutUnlock_ResolvesCultureFromUserSettingsLanguage`
- `ResendEmailService_RetriesOnTransient_NoRetryOn4xx`
- `ResendEmailService_FailsLoudInProductionWithoutApiKey`
- `EmailByUserRateLimit_429sAfter5thSend`
- `EmailByIpRateLimit_429sAfter10thSend`
- `EmailByUserRateLimit_429s_Across_Branches_Have_Equal_Argon2id_Call_Count` (Stage 6.16 preservation)
- `EmailChangeRequest_PartitionsByUserId` (auth-gated path uses UserId, not email)
- `ResendWebhook_RejectsBadSignature`
- `ResendWebhook_AcceptsValidSignature_RecordsEvent`
- `ResendWebhook_BouncedEvent_FlipsEmailConfirmed`
- `ResendWebhook_TimestampOutsideTolerance_Rejects`

---

## Deferred / out of scope

Per `feedback_persist_deferred_decisions.md` + `feedback_defer_work_to_all_three_docs.md`, the following deferrals get persisted in the planning + roadmap layers:

1. **Resend sending domain verification + SPF/DKIM/DMARC publication** → new line in `roadmap-phase-three.md § Stage 16` ("Publish SPF/DKIM/DMARC records per `docs/runbooks/email-dns-setup.md` on the registered sending domain"); cross-referenced from `planning-phase3.md § Batch 5`.
2. **EmailsResource keys + call-sites for emails not yet firing** (registration confirmation, TOTP enrolled/disabled, backup-codes regenerated, new-session alert, GDPR-export-ready, account-erasure-confirmation) → already on Stage 9 / 10 / 12 / 13 lines; each line gets the note "Reserve `EmailsResource` keys at the time the call-site lands; do not add empty keys ahead of time".
3. **Digest-email infrastructure** (one-click unsubscribe, opt-in preference column, scheduled-send pipeline) → `planning-future.md` (Phase 4+ marketing-class email work).

---

## Open questions

None at time of writing. All scoping decisions resolved in the 2026-05-14 brainstorming session:

- Scope: plumbing + localize existing six emails only (not the new emails for unsent flows).
- DNS: runbook ships in Stage 8; publication deferred to Stage 16.
- Resend: API key only, no verified domain yet → ship with `onboarding@resend.dev` as dev/test default; production sender configured at Stage 16.
- Secrets: `dotnet user-secrets` dev, `Email__Resend__ApiKey` env var prod.
- Template extraction: full move to resx + composer; the nine `Build*Email` helpers in auth services all delete.
- Brand: "Ceres" in every user-facing email, never "Project Ceres".
