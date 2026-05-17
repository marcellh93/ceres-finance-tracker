# Stage 9.3 — `/register` + email verification — design

**Status:** Draft — 2026-05-18. Spec only; implementation runs in a follow-up session per the existing `[ ]` line at `docs/roadmap-phase-three.md:954` (Stage 9 sub-stage 9.3). The roadmap row IS the receiving checkbox + tripwire (Stage 9 close-out cannot complete with 9.3 unticked).
**Roadmap anchor:** `docs/roadmap-phase-three.md` → Stage 9 → sub-stage 9.3
**Originating context:** Stage 6a spec § "Why no auto-sign-in?" (the 6a-deferred slice this closes); Stage 8 carry-forward at roadmap lines 1078–1082; `docs/security-model.md` § Registration

---

## Why this stage is bigger than 9.2 / 9.4

9.2 and 9.4 are pure SPA stages — the server endpoints they call already exist. 9.3 is different: the server-side **email-confirmation token pipeline does not exist at all**. The Stage 8 carry-forward lists "wire registration-confirmation email at `POST /api/auth/register`" as a single bullet, but in practice that bullet expands into:

1. A new entity `EmailConfirmationToken` mirroring `PasswordResetToken` (with the Stage 6.15 / 9.1.5.a `TokenLookup` indexed-lookup pattern — non-negotiable to avoid an Argon2id-O(N) DoS).
2. An EF migration adding the table + indexes.
3. A new service `EmailConfirmationService` with `IssueAsync` + `ConfirmAsync` mirroring `PasswordResetService`.
4. Two new HTTP endpoints under `AuthController` or a new `EmailVerificationController`: `POST /api/auth/email/verify` (consume token) and `POST /api/auth/email/verify/resend` (re-issue).
5. Wiring in the existing `Register` handler at `AuthController.cs:75-113`: on `CreateAsync` success, before `tx.CommitAsync`, issue a token and queue the email send.
6. `RegistrationConfirmation` EN+ES resx entries in `EmailsResource.{en,es}.resx`.
7. A `RegistrationConfirmation` value added to `EmailTemplateKey` enum (`ProjectCeres/Common/Email/EmailTemplateKey.cs`).
8. DI registration for `EmailConfirmationService` in `Program.cs`.
9. ~10 integration tests (token lifecycle, idempotency, tamper-resistance, rate limits, expiry, anti-enumeration on resend).
10. Two SPA pages: `/register` (form) and `/email-verify` (token-consuming page).
11. ~8 SPA tests.

That's a stage's worth of work on its own. Splitting it from 9.2 / 9.4 is the only path that produces working software at the end without rushing security-sensitive token plumbing.

---

## Server-side design

### Entity: `EmailConfirmationToken`

Mirror of `PasswordResetToken` minus `MfaVerifiedAt` (email confirmation never gates on TOTP). New file `ProjectCeres/Models/EmailConfirmationToken.cs`:

```csharp
using ProjectCeres.Common;

namespace ProjectCeres.Models;

/// <summary>
/// One row per active or recently-consumed email-confirmation token issued
/// during registration. TokenHash is Argon2id-hashed; TokenLookup is
/// HMAC-SHA256(serverSecret, rawToken) with a unique index so /verify finds
/// the row in O(1) (Stage 6.15 / 9.1.5.a pattern). Single-use: ConsumedAt
/// is set inside the same DB transaction as `user.EmailConfirmed = true`.
/// </summary>
public sealed class EmailConfirmationToken : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] TokenLookup { get; set; } = Array.Empty<byte>();
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
```

Add to `AppDbContext`: `DbSet<EmailConfirmationToken> EmailConfirmationTokens { get; set; }` and an `OnModelCreating` block mirroring `PasswordResetToken`'s — `HasIndex(e => e.TokenLookup).IsUnique()` and `HasIndex(e => e.UserId)` for the supersede query.

### Migration: `AddEmailConfirmationTokens`

```sql
CREATE TABLE "EmailConfirmationTokens" (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
    "TokenLookup" bytea NOT NULL,
    "TokenHash" text NOT NULL,
    "CreatedAt" timestamp without time zone NOT NULL,
    "ExpiresAt" timestamp without time zone NOT NULL,
    "ConsumedAt" timestamp without time zone NULL
);
CREATE UNIQUE INDEX "IX_EmailConfirmationTokens_TokenLookup"
    ON "EmailConfirmationTokens" ("TokenLookup");
CREATE INDEX "IX_EmailConfirmationTokens_UserId"
    ON "EmailConfirmationTokens" ("UserId");
```

Mirrors `20260511155005_AddTokenLookup.cs` (Stage 6.15) and `20260517033235_AddLockoutUnlockTokenLookup.cs` (Stage 9.1.5.a) in shape. `Down()` drops the table cleanly.

### Service: `EmailConfirmationService`

`ProjectCeres/Common/Authentication/EmailConfirmationService.cs`. Public surface:

```csharp
public sealed class EmailConfirmationService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
    public const int EmailRateLimit = 5;

    public sealed class RateLimitedException : Exception { public int RetryAfterSeconds { get; } }

    // Called from Register handler; never throws on email-send failure (token row already committed).
    public Task IssueAsync(Guid userId, string email, string verifyUrlBase, CancellationToken ct);

    // Called from /api/auth/email/verify/resend. Anti-enumerating: returns 204 whether or not the email exists.
    public Task RequestResendAsync(string email, CancellationToken ct);

    // Called from /api/auth/email/verify. Returns outcome union (Success, InvalidToken).
    public Task<EmailConfirmationConfirmOutcome> ConfirmAsync(string rawToken, CancellationToken ct);
}

public abstract record EmailConfirmationConfirmOutcome
{
    public sealed record Success(Guid UserId) : EmailConfirmationConfirmOutcome;
    public sealed record InvalidToken : EmailConfirmationConfirmOutcome;
}
```

Internal implementation mirrors `PasswordResetService` line-for-line for: per-user semaphore, supersede unconsumed tokens, Argon2id constant-time floor on the miss path, `TokenLookup` indexed query, tamper-resistance verify of `TokenHash`, transaction around `EmailConfirmed = true` + `ConsumedAt`. The Argon2id mirror on the unknown branch is critical — without it, registration timing leaks "this email is known".

**Critical detail (don't drop):** `IssueAsync` is called from inside `Register`'s existing transaction. The token row insert must happen BEFORE `tx.CommitAsync`. The email send happens AFTER commit (failure to send must not roll back the user creation).

### Token generator

Reuse the existing `PasswordResetTokenGenerator` pattern. We need a parallel `EmailConfirmationTokenGenerator` (or a generic `TokenGenerator<TKind>`). Path of least resistance: copy `PasswordResetTokenGenerator.cs` to `EmailConfirmationTokenGenerator.cs`, change the class name. The "DRY" abstraction is tempting but the user explicitly prefers concrete duplication over premature abstraction (CLAUDE.md "Don't add features, refactor, or introduce abstractions beyond what the task requires").

### Endpoints

New controller `EmailVerificationController` under `ProjectCeres/Controllers/Api/`. Two endpoints:

```csharp
[ApiController]
[Route("api/auth/email")]
public sealed class EmailVerificationController : ControllerBase
{
    private readonly EmailConfirmationService _service;
    public EmailVerificationController(EmailConfirmationService service) => _service = service;

    [HttpPost("verify"), AllowAnonymous, PreAuthCallSite("Email.Verify")]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Verify([FromBody] EmailVerifyRequest request) { ... }

    [HttpPost("verify/resend"), AllowAnonymous, PreAuthCallSite("Email.VerifyResend")]
    [EnableRateLimiting(AuthRateLimitPolicies.EmailByUser)]
    [ApplyEmailIpRateLimit]
    public async Task<IActionResult> RequestResend([FromBody] EmailVerifyResendRequest request) { ... }
}
```

`Verify` returns `204` on success; `401 INVALID_VERIFICATION_TOKEN` on miss. `RequestResend` returns `204` always (anti-enumeration), `429 RATE_LIMITED` with `Retry-After` on rate-limit hit. Mirrors `PasswordResetController` exactly.

### Wiring into `Register`

`AuthController.cs:75-113`. Currently the handler returns `204` on duplicate-username (anti-enumeration) and on fresh-create. Insert a single call AFTER `await tx.CommitAsync` and BEFORE `return NoContent()`:

```csharp
var verifyUrlBase = $"{Request.Scheme}://{Request.Host}";
await _emailConfirmation.IssueAsync(user.Id, user.Email!, verifyUrlBase, HttpContext.RequestAborted);
```

The duplicate-username path also fires `IssueAsync` for the EXISTING user — same anti-enumeration logic as Stage 6c follow-up at `AuthController.cs:95-97` ("send 'someone tried to register with your email' notice"). For 9.3 scope, this expansion is deferred — the immediate need is the fresh-register path.

### Resx entries

Add to `EmailsResource.en.resx`:

```xml
<data name="RegistrationConfirmation.Subject" xml:space="preserve">
  <value>Confirm your Ceres email address</value>
</data>
<data name="RegistrationConfirmation.BodyText" xml:space="preserve">
  <value>Welcome to Ceres. Click the link below within 30 minutes to confirm your email and finish signing up:

{0}

If you didn't sign up, you can ignore this email.</value>
</data>
<data name="RegistrationConfirmation.BodyHtml" xml:space="preserve">
  <value>&lt;p&gt;Welcome to Ceres. &lt;a href="{0}"&gt;Confirm your email&lt;/a&gt; within 30 minutes to finish signing up.&lt;/p&gt;&lt;p&gt;If you didn't sign up, you can ignore this email.&lt;/p&gt;</value>
</data>
```

Spanish mirror with the same key names. Add `RegistrationConfirmation` to `EmailTemplateKey` enum at `ProjectCeres/Common/Email/EmailTemplateKey.cs:3-14`.

### DI registration

In `Program.cs`, alongside `builder.Services.AddSingleton<PasswordResetTokenGenerator>()` (or wherever the password-reset registrations live):

```csharp
builder.Services.AddSingleton<EmailConfirmationTokenGenerator>();
builder.Services.AddScoped<EmailConfirmationService>();
```

### Verify URL format

Mirror the password-reset format. `{verifyUrlBase}/app/email-verify#token={raw}`. Fragment, not query — keeps the token out of server logs and Referer headers.

---

## SPA design

### `/register` page

`pages/auth/Register.tsx`. Form fields:
- Email
- Password (with policy hint: "≥ 8 characters, not previously breached")

Submit posts to `POST /api/auth/register`. Server returns `204` regardless of whether the email was new or duplicate (anti-enumeration). UI behaviour:

- `204`: replace form with "Check your inbox" success block, similar to 9.4's request success. Body: "We've sent a verification link to <email>. Click the link to finish creating your account."
- `422 VALIDATION_ERROR` with `details: [{field: "Password", message: "..."}]`: map to react-hook-form field errors. Password policy failures (PwnedPasswords / short) go here.
- Network / 5xx: inline error.

"Back to sign in" link.

### `/email-verify` page

`pages/auth/EmailVerify.tsx`. On mount, reads `location.hash` for `token=...`. Behaviour:

- No token in hash → render an error block: "This verification link is invalid. Request a new one." with a button "Resend verification email" (prompts for email, posts to `/api/auth/email/verify/resend`).
- Token present → POST `/api/auth/email/verify` with `{ token }`. While in flight, show a spinner + "Verifying your email..."
- `204` → success block: "Email verified. You can now sign in." + Link to `/login`.
- `401 INVALID_VERIFICATION_TOKEN` → error block with the same "Resend verification email" affordance.
- Network → retry-able inline error.

### Route registration

In `App.tsx`:

```tsx
<Route path="register" element={<Register />} />     // existing placeholder → Register
<Route path="email-verify" element={<EmailVerify />} />  // new route
```

Delete `RegisterPlaceholder.tsx` and its dead i18n keys at `auth.placeholders.register.*`.

### Update `Login.tsx`

The existing `onResendVerification` handler at `Login.tsx:90-109` was a Phase-1 placeholder ("ships in Phase 2"). 9.3 wires it for real: POST `/api/auth/email/verify/resend`. The function shell stays; the 429 + `Retry-After` countdown path is in scope (reuse the pattern from 9.2's `submitTotp` and 9.4's request flow).

---

## Tests (TDD)

### Backend integration tests

`ProjectCeres.Tests/Integration/EmailConfirmationTests.cs`. ~10 tests:

1. Register → token row exists with non-null `TokenLookup`, non-null `TokenHash`, `ConsumedAt is null`, `ExpiresAt = CreatedAt + 30 min`.
2. Verify with valid token → 204, `user.EmailConfirmed = true`, `ConsumedAt` set.
3. Verify with expired token → 401 INVALID_VERIFICATION_TOKEN, user.EmailConfirmed unchanged.
4. Verify with consumed token → 401 INVALID_VERIFICATION_TOKEN.
5. Verify with random non-matching token → 401 INVALID_VERIFICATION_TOKEN; constant-time floor (one Argon2id verify against dummy).
6. Verify with token where TokenHash was tampered → 401 INVALID_VERIFICATION_TOKEN (tamper-resistance: `_tokens.Verify` second-line defence).
7. Resend with unknown email → 204 (anti-enumeration).
8. Resend with known email → 204; previous unconsumed token row marked consumed (supersede); new row exists.
9. Resend rate limit: 6th request in an hour → 429 with `Retry-After` header > 0.
10. Re-registering same email after the original token expired → 204 + new token row issued (the existing 6c "duplicate path" already returns 204; we now also send a token to the *original* user).

### SPA tests

`pages/auth/Register.test.tsx` — ~5 tests:

1. Renders email + password + submit + back-to-sign-in.
2. 204 replaces form with success block referencing the typed email.
3. 422 VALIDATION_ERROR on Password → field error.
4. Network → inline error.
5. zod: password < 8 chars → field error, no fetch.

`pages/auth/EmailVerify.test.tsx` — ~5 tests:

1. No hash → invalid-link error block + resend button.
2. Token in hash → fires POST `/api/auth/email/verify` with that token.
3. 204 → success block + sign-in link.
4. 401 INVALID_VERIFICATION_TOKEN → invalid-link error block + resend.
5. Resend button → opens email prompt → POST `/api/auth/email/verify/resend` → success acknowledgement.

---

## i18n keys to add

Under `auth.register.*`: `title`, `description`, `emailLabel`, `passwordLabel`, `passwordHint`, `submit`, `submitting`, `successTitle`, `successBody`, `backToSignIn`, `errors.network`.

Under `auth.emailVerify.*`: `title`, `verifyingMessage`, `successTitle`, `successBody`, `signInLink`, `invalidTitle`, `invalidBody`, `resendButton`, `resendPromptLabel`, `resendSuccess`, `errors.network`.

Spanish mirror.

---

## Risks + mitigations

| Risk | Mitigation |
|---|---|
| Anti-enumeration timing leak on `/register` if `IssueAsync` runs only for fresh users and not duplicates. | The duplicate path returns `204` without `IssueAsync` today. The full anti-enum fix is the Stage 6c "send 'someone tried to register with your email' notice". That fix is its own follow-up; 9.3 ships the fresh-register path and documents the duplicate-path leak. |
| Argon2id-O(N) DoS recurrence on `/email/verify`. | Use the `TokenLookup` indexed-lookup pattern (Stage 6.15 / 9.1.5.a). Tests #5 and #6 pin the contract. |
| Email-send failure rolling back user creation. | `_email.SendAsync` is called AFTER `tx.CommitAsync`. Token row is committed before send. Send failures are logged but never thrown — mirrors `PasswordResetService.cs:174-178`. |
| `RequireConfirmedEmail = true` blocks login of in-flight users. | Already documented in roadmap line 954 ("completes a 6a-deferred slice"). This is the correct security posture — users MUST verify before they can sign in. |
| The 9.1.5.h `ILookupNormalizer` rule (any normalizer change requires a same-commit data-migration). | This stage adds a new table but doesn't change the normalizer. Documenting the rule as a tripwire reminder in the spec; no migration needed for 9.3 itself. |

---

## Items folded INTO 9.3 scope (no deferral)

The first draft of this spec tried to mark the duplicate-username anti-enumeration notice as out-of-scope by citing the existing Stage-6c FIXME comment at `AuthController.cs:95-97`. That fails the project's deferral rule: a code-side FIXME is necessary but not sufficient — the work also needs a roadmap `[ ]` line. None exists for that item.

Fold it IN. 9.3's scope therefore covers the duplicate-username path too: when `Register` returns 204 on a duplicate username, also call `EmailConfirmationService.IssueAsync` against the EXISTING user's record (re-sending the verification email is the right behaviour anyway — the legitimate user may have lost their original token). Anti-enumeration timing parity is preserved because both branches now do the same work.

Implementation note: the existing FIXME at `AuthController.cs:95-97` is resolved as part of 9.3 — the verification email IS the duplicate-path notice, by design. Update the comment text to reflect the new behaviour or delete it during implementation.

## Architecturally out of scope (not a deferral)

- Any change to ASP.NET Identity's `EmailConfirmed` flag flow. Reuse Identity's existing field as-is. This is a "we don't need this" decision, not a "we'll do it later".

## Scheduled in a downstream stage (with receiving `[ ]` line)

- "First-login after verify" onboarding redirect. Receiving entry: Stage 15.5 — Onboarding wizard at `docs/roadmap-phase-three.md:1518`. The roadmap's Stage 9 line 1022 ("On successful enrolment: redirect to onboarding (Stage 15.5)...") already cross-references Stage 15.5 as the owner. Tripwire: that line itself is the receiving `[ ]` — Stage 15.5's close-out cannot complete without it.

---

## Definition of Done

1. `dotnet test --filter "FullyQualifiedName~ProjectCeres.Tests.Integration.EmailConfirmation"` → all green.
2. `dotnet test` (full suite) → all green (1098 passed today; 1108 after 9.3).
3. `pnpm test` (full client suite) → green.
4. `pnpm build` → green.
5. The roadmap's `/register` block (lines 991–996) ticked where automated tests cover them.
6. Manual: a real registration in the dev DB lands an `EmailConfirmationToken` row; the verification link in the dev MailDrop folder consumes the token; the user can then sign in.

---

## Plan handoff

The implementation plan is the next document; it will break this spec into 6–8 sub-tasks following TDD (write the failing test, write minimum code to pass, commit). Estimated execution time: 3–4 focused hours.
