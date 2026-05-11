# Stage 6c Sub-stage 6.12 — Email-Address-Change Flow

**Date:** 2026-05-10
**Stage:** Phase 3, Stage 6c (Identity infrastructure, Batch 3b continued)
**Sub-stage:** 6.12 (email-address-change)
**Author:** brainstormed with user, approved 2026-05-10
**Plan:** `docs/superpowers/plans/2026-05-10-stage-6-12-email-change-plan.md`

## 1. Scope

Implement the email-address-change flow per `security-model.md` § Email Address Change.

In scope:
- Three new API endpoints — `POST /api/auth/email-change/request|confirm|revoke`
- `EmailChangeToken` entity (Argon2id-hashed, dual-purpose: VerifyNew 30-min + RevokeOld 7-day)
- `EmailChangeService` orchestration
- `EmailChangeTokenGenerator` mirroring `PasswordResetTokenGenerator`
- Per-IP `AuthLoginByIp` rate limit (10/min) + service-side per-new-email window (5/hour)
- All-sessions-revoked-on-confirm
- Reauthentication gate on `/request` via existing `[RequireRecentAuth]` (Stage 6c.2)
- Cross-feature: successful `/password-reset/confirm` cancels any pending email-change
- Notification emails to old + new addresses across the lifecycle (verify, revoke, confirm)

Out of scope (deferred to other stages):
- UI (`/settings/email` SPA page) — Stage 9 / Stage 12
- `AuditLog` entries for email-change — Stage 6.14 (writer service ships there)
- Real email provider (SMTP/SendGrid/etc.) — Stage 8
- EN/ES `.resx` template files — Stage 8 (hand-written EN strings live inline until then)

## 2. Architecture & components

### 2.1 Token model — single table, `Purpose` discriminator

**Decision:** one `EmailChangeTokens` table with a `Purpose` enum column (`VerifyNew=1`, `RevokeOld=2`), not two tables.

**Justification:**
- One DDL/migration to maintain.
- Index parity with `PasswordResetTokens`: `(UserId, ConsumedAt)` + `ExpiresAt`. Query plan reviewers learn one shape, not three.
- Sibling consume on confirm/revoke is one `ExecuteUpdateAsync` keyed by `(UserId, NewEmail, Purpose=other)` instead of a cross-table join.
- Supersession on a new `/request` is one `Where(UserId == … && ConsumedAt == null)` sweep regardless of which side a stale row sits on.

The cost — a tiny enum column — is paid for by simpler invariants.

### 2.2 New entity — `EmailChangeToken`

Location: `ProjectCeres/Models/EmailChangeToken.cs`.

```csharp
public enum EmailChangeTokenPurpose { VerifyNew = 1, RevokeOld = 2 }

public sealed class EmailChangeToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public EmailChangeTokenPurpose Purpose { get; set; }
    public string NewEmail { get; set; } = "";  // lowercase-normalized
    public string TokenHash { get; set; } = ""; // Argon2id, max 512
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
```

DbContext config follows `ConfigurePasswordResetEntities`:

```csharp
modelBuilder.Entity<EmailChangeToken>(b =>
{
    b.HasKey(e => e.Id);
    b.HasIndex(e => new { e.UserId, e.ConsumedAt });
    b.HasIndex(e => e.ExpiresAt);
    b.Property(e => e.NewEmail).HasMaxLength(256);
    b.Property(e => e.TokenHash).HasMaxLength(512);
    b.Property(e => e.Purpose).HasConversion<int>();
});
```

Add `DbSet<EmailChangeToken> EmailChangeTokens => Set<EmailChangeToken>();` to `AppDbContext`. No FK to `AspNetUsers` (parity with `PasswordResetToken`).

### 2.3 New service — `EmailChangeService`

Location: `ProjectCeres/Common/Authentication/EmailChangeService.cs`.

Public surface:

```csharp
Task<EmailChangeRequestOutcome> RequestAsync(
    Guid userId, string newEmail,
    string ip, string userAgent,
    string verifyUrlBase, string revokeUrlBase,
    CancellationToken ct);

Task<EmailChangeConfirmOutcome> ConfirmAsync(string rawToken, CancellationToken ct);
Task<EmailChangeRevokeOutcome> RevokeAsync(string rawToken, CancellationToken ct);
```

Discriminated outcomes (`ProjectCeres/Common/Authentication/EmailChangeOutcomes.cs`):

```csharp
public abstract record EmailChangeRequestOutcome
{
    public sealed record Accepted : EmailChangeRequestOutcome;
    public sealed record EmailAlreadyInUse : EmailChangeRequestOutcome;
    public sealed record EmailUnchanged : EmailChangeRequestOutcome;
}
public abstract record EmailChangeConfirmOutcome
{
    public sealed record Success : EmailChangeConfirmOutcome;
    public sealed record InvalidToken : EmailChangeConfirmOutcome;
    public sealed record EmailAlreadyInUse : EmailChangeConfirmOutcome;
}
public abstract record EmailChangeRevokeOutcome
{
    public sealed record Success : EmailChangeRevokeOutcome;
    public sealed record InvalidToken : EmailChangeRevokeOutcome;
}
```

Class statics (parity with `PasswordResetService`):

```csharp
public static readonly TimeSpan VerifyTokenLifetime = TimeSpan.FromMinutes(30);
public static readonly TimeSpan RevokeTokenLifetime = TimeSpan.FromDays(7);
public static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
public const int EmailRateLimit = 5;

private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

public sealed class RateLimitedException(int retryAfterSeconds)
    : Exception("Email-change rate limit exceeded.")
{ public int RetryAfterSeconds { get; } = retryAfterSeconds; }
```

Constructor injects: `UserManager<ApplicationUser>`, `AppDbContext`, `Argon2idPasswordHasher`, `EmailChangeTokenGenerator`, `IEmailService`, `IMemoryCache`, `ILogger<EmailChangeService>`. **Not** `SignInManager`, `TotpReplayGuard`, `FailedLoginRecorder` — none of those are in scope for email-change.

### 2.4 New helper — `EmailChangeTokenGenerator`

Location: `ProjectCeres/Common/Authentication/EmailChangeTokenGenerator.cs`. Verbatim shape copy of `PasswordResetTokenGenerator`: `Generate()` returns 256-bit base64url-encoded token, `Hash()` runs Argon2id, `Verify()` constant-time compares.

A separate class (rather than reusing `PasswordResetTokenGenerator`) lets the architecture test `EmailChangeController_does_not_call_PasswordHasher_directly` discriminate cleanly and keeps DI lifetime decisions independent.

### 2.5 New controller — `EmailChangeController`

Location: `ProjectCeres/Controllers/Api/EmailChangeController.cs`. Route prefix `/api/auth/email-change`.

```csharp
[ApiController]
[Route("api/auth/email-change")]
public sealed class EmailChangeController : ControllerBase
{
    [HttpPost("request"), Authorize, RequireRecentAuth]
    public async Task<IActionResult> RequestChange([FromBody] EmailChangeRequest body) { ... }

    [HttpPost("confirm"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> ConfirmChange([FromBody] EmailChangeConfirmRequest body) { ... }

    [HttpPost("revoke"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> RevokeChange([FromBody] EmailChangeRevokeRequest body) { ... }
}
```

Action names `RequestChange` / `ConfirmChange` / `RevokeChange` avoid `ControllerBase.Request` shadowing.

`AutoValidateAntiforgeryTokenAttribute` is registered globally in `Program.cs:185`, so CSRF is enforced without per-action `[ValidateAntiForgeryToken]`. The `[ApiController]` `InvalidModelStateResponseFactory` (Program.cs:36-55) auto-returns 422 for any `[Required]` / `[StringLength]` / `[EmailAddress]` violation — no manual `ModelState.IsValid` check is needed in the action body.

### 2.6 DTOs

- `ProjectCeres/ViewModels/Auth/EmailChangeRequest.cs` — `[Required, EmailAddress, StringLength(256)] string NewEmail`.
- `ProjectCeres/ViewModels/Auth/EmailChangeConfirmRequest.cs` — `[Required, StringLength(128)] string Token`.
- `ProjectCeres/ViewModels/Auth/EmailChangeRevokeRequest.cs` — `[Required, StringLength(128)] string Token`.

### 2.7 Endpoint contracts

| Endpoint | Auth | Rate limit | Body | Success | Failures |
|---|---|---|---|---|---|
| `POST /api/auth/email-change/request` | `[Authorize]` + `[RequireRecentAuth]` | per-IP `AuthLoginByIp` (global) + service-side per-new-email window (5/hour) | `{ newEmail }` | `202 Accepted` `{ data: { message } }` | `401 REAUTH_REQUIRED` if stale claim; `422 EMAIL_UNCHANGED`; `422 EMAIL_ALREADY_IN_USE`; `422 VALIDATION_ERROR`; `429 RATE_LIMITED` |
| `POST /api/auth/email-change/confirm` | `[AllowAnonymous]` (token IS the auth) | per-IP `AuthLoginByIp` | `{ token }` | `204 No Content` | `401 INVALID_EMAIL_CHANGE_TOKEN`; `422 EMAIL_ALREADY_IN_USE`; `422 VALIDATION_ERROR` |
| `POST /api/auth/email-change/revoke` | `[AllowAnonymous]` (token IS the auth) | per-IP `AuthLoginByIp` | `{ token }` | `204 No Content` | `401 INVALID_EMAIL_CHANGE_TOKEN`; `422 VALIDATION_ERROR` |

### 2.8 Error codes (additions to `docs/api-contract.md` § Canonical error codes)

| Code | Status | Returned by | When |
|---|---|---|---|
| `INVALID_EMAIL_CHANGE_TOKEN` | 401 | `POST /api/auth/email-change/confirm`, `POST /api/auth/email-change/revoke` | Token unknown, malformed, expired, or already consumed. Stage 6.12. |
| `EMAIL_ALREADY_IN_USE` | 422 | `POST /api/auth/email-change/request`, `POST /api/auth/email-change/confirm` | The new email address is already registered to another account. Stage 6.12. |
| `EMAIL_UNCHANGED` | 422 | `POST /api/auth/email-change/request` | The submitted new email equals the current address-of-record (case-insensitive against `NormalizedEmail`). Stage 6.12. |

### 2.9 Email templates (inline EN-only, hand-written)

Five subjects, all sent via `IEmailService.SendAsync`:

- `BuildVerifyNewEmail(newEmail, verifyUrl)` → "Confirm your new Project Ceres email address" → new address. Body contains the verify URL with `#token=` fragment.
- `BuildRevokeOldEmail(oldEmail, newEmail, revokeUrl)` → "An email change was requested on your Project Ceres account" → old address. Body discloses old + new email and the revoke URL with `#token=` fragment.
- `BuildChangeConfirmedEmail(newEmail)` → "Your Project Ceres email address was changed" → new address (now address-of-record). Notification only, no link.
- `BuildChangeConfirmedToOldEmail(oldEmail, newEmail)` → "Your Project Ceres email address was changed" → old address. Per security-model.md "Notify the old address on successful completion of the change."
- `BuildRevokeNotificationToOldEmail(oldEmail)` → "Email change cancelled" → old address. Notification only, no link.
- `BuildEmailChangeCancelledByPasswordResetEmail(oldEmail)` (lives in `PasswordResetService`) → "Pending email change cancelled" → old address. Sent when password-reset confirms while an email-change is pending.

All URLs use the `#token=` fragment, never the query string (parity with password-reset; browsers don't send fragments to servers or include them in `Referer`).

### 2.10 Concurrency model

`EmailChangeService` carries a static `ConcurrentDictionary<Guid, SemaphoreSlim>` keyed by `userId`, mirroring `PasswordResetService._userLocks`.

- `RequestAsync` enters the semaphore around supersede + insert.
- `ConfirmAsync` enters the semaphore around the re-read + Identity update + sibling consume + session revoke.
- `RevokeAsync` enters the semaphore around the dual-row consume.

Two concurrent `/confirm` with the same raw token → one wins (sets `ConsumedAt` synchronously inside the transaction), the other observes the consumed state on re-read and returns `InvalidToken`. Two concurrent `/request` for the same user → one wins, the other supersedes the first via the same invalidate-prior step.

### 2.11 Rate limits

- **Per-IP** — applied at the controller via `[EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]` on `/confirm` and `/revoke` (10/min/IP sliding window, reused). `/request` inherits no explicit per-IP attribute because it's `[RequireRecentAuth]`-gated; an attacker would need a fresh reauth claim for each request.
- **Per-new-email** — service-side `MemoryCache` sliding window of 5 calls/hour, keyed by lowercase-normalized **new** email. Reasoning: the abuse vector is spamming a known address (the new address) with verification mail. Implementation: copy `PasswordResetService.EnforceEmailRateLimit` with cache key prefix swapped to `emailchange:rate:`. Throws `RateLimitedException`; controller maps to 429 with `Retry-After`.

### 2.12 DI wiring

Add to `Program.cs`, after the password-reset registrations:

```csharp
builder.Services.AddScoped<EmailChangeTokenGenerator>();
builder.Services.AddScoped<EmailChangeService>();
```

No new policies, no new partitioners. Production still has no `IEmailService` registered — startup throws at DI resolution. This intentionally blocks production deployment until Stage 8 wires the real provider.

## 3. Data flow

### 3.1 `POST /api/auth/email-change/request`

1. Reauth gate (`[RequireRecentAuth]`) fires first. Stale `LastReauthAt` claim → 401 `REAUTH_REQUIRED` from `RecentAuthMiddlewareResultHandler`. No token rows written.
2. `[ApiController]` factory runs ModelState validation. `[Required]` / `[EmailAddress]` / `[StringLength(256)]` violation → 422 `VALIDATION_ERROR`.
3. Controller parses `userId` from `User.FindFirstValue(ClaimTypes.NameIdentifier)` and IP/UA/host from `HttpContext`. Calls `EmailChangeService.RequestAsync`.
4. Service:
   - Normalize `newEmail` (trim + `ToLowerInvariant`).
   - Empty after normalize → dummy hash, return `Accepted` (defensive).
   - `UserManager.FindByIdAsync(userId)`; null → dummy hash, return `Accepted` (defensive — `[Authorize]` should have prevented).
   - `newEmail` matches `user.NormalizedEmail` (case-insensitive) → return `EmailUnchanged`.
   - `UserManager.FindByEmailAsync(newEmail)` not null → return `EmailAlreadyInUse`.
   - `EnforceEmailRateLimit(normalizedNewEmail)` — throws `RateLimitedException` if exceeded.
   - Acquire user semaphore.
   - Supersede prior unused tokens: `WHERE UserId == user.Id && ConsumedAt == null SET ConsumedAt = UtcNow`.
   - Generate two raw tokens, hash each with Argon2id.
   - Insert two rows in one `SaveChangesAsync(ct)`: VerifyNew with `ExpiresAt = now + 30min`, RevokeOld with `ExpiresAt = now + 7d`. Both rows share `UserId` and `NewEmail`.
   - Release semaphore.
   - Build URLs: `{base}/app/email-change/confirm#token={raw1}` and `{base}/app/email-change/revoke#token={raw2}`.
   - Send `BuildVerifyNewEmail` to new address, `BuildRevokeOldEmail` to old address. Each wrapped in try/catch + log.
   - Return `Accepted`.
5. Controller maps outcome → 202 `{ data: { message: "Verification email sent. Check your old and new inboxes." } }`.

### 3.2 `POST /api/auth/email-change/confirm`

1. `[ApiController]` factory: `[Required]` / `[StringLength(128)]` violation → 422.
2. Controller calls `EmailChangeService.ConfirmAsync(token, ct)`.
3. Service:
   - Empty token → dummy hash, return `InvalidToken`.
   - Load candidates: `WHERE Purpose == VerifyNew && ConsumedAt == null && ExpiresAt > now`.
   - First `_tokens.Verify(rawToken, c.TokenHash)` match wins. No match → dummy hash if zero candidates; return `InvalidToken`.
   - Acquire user semaphore on `match.UserId`.
   - Re-read row by `Id` `AsNoTracking`. Null/consumed/expired → return `InvalidToken` (lost race).
   - `UserManager.FindByIdAsync(match.UserId)`; null → return `InvalidToken`.
   - **Re-check collision:** `UserManager.FindByEmailAsync(match.NewEmail)`. Not null AND `result.Id != user.Id` → return `EmailAlreadyInUse`. Token NOT consumed (caller can `/revoke`).
   - **Update Identity row:** `await UserManager.SetEmailAsync(user, match.NewEmail)` then `await UserManager.SetUserNameAsync(user, match.NewEmail)`. (Registration sets `UserName == Email` per `AuthController:65`; updating only `Email` would leave `FindByNameAsync(oldEmail)` resolving the user.) Set `user.EmailConfirmed = true`. `await UserManager.UpdateAsync(user)`.
   - **Consume matched VerifyNew row:** `ExecuteUpdateAsync` by `Id`.
   - **Consume sibling RevokeOld row:** `ExecuteUpdateAsync` `WHERE UserId == user.Id && Purpose == RevokeOld && NewEmail == match.NewEmail && ConsumedAt == null`.
   - **Bulk-revoke sessions:** `ExecuteUpdateAsync` on `UserSessions` `WHERE UserId == user.Id && RevokedAt == null SET RevokedAt = UtcNow` (verbatim from `PasswordResetService.ConfirmAsync:312-315`).
   - `UserManager.UpdateSecurityStampAsync(user)`.
   - **Do NOT** call `ResetAccessFailedCountAsync` / `SetLockoutEndDateAsync` (negative-assertion test pins this).
   - **Do NOT** call `SignInManager.SignOutAsync` — endpoint is anonymous; SecurityStamp regen invalidates any stale cookie within 5 min via `SecurityStampValidator`.
   - Send `BuildChangeConfirmedEmail` to new address. Try/catch + log.
   - Release semaphore; return `Success`.
4. Controller maps `Success` → 204; `InvalidToken` → 401 `INVALID_EMAIL_CHANGE_TOKEN`; `EmailAlreadyInUse` → 422 `EMAIL_ALREADY_IN_USE`.

### 3.3 `POST /api/auth/email-change/revoke`

1. `[ApiController]` factory: `[Required]` / `[StringLength(128)]` → 422.
2. Service `RevokeAsync(token, ct)`:
   - Empty token → dummy hash, `InvalidToken`.
   - Candidates: `WHERE Purpose == RevokeOld && ConsumedAt == null && ExpiresAt > now`. Verify hashes; no match → dummy hash if zero candidates; return `InvalidToken`.
   - Acquire user semaphore on `match.UserId`.
   - Re-read row; lost-race → `InvalidToken`.
   - **Consume both rows atomically:** `ExecuteUpdateAsync` `WHERE UserId == match.UserId && NewEmail == match.NewEmail && ConsumedAt == null SET ConsumedAt = UtcNow` (matches both Verify + Revoke siblings in one statement).
   - Load `user`; capture `oldEmail = user.Email`.
   - Send `BuildRevokeNotificationToOldEmail(oldEmail)` to OLD address only. Try/catch + log.
   - **`user.Email` is NOT updated** (critical). **No** session revocation, **no** SecurityStamp regen. Revoke is a cancel-pending-change, not a security event for the legitimate user.
   - Release semaphore; return `Success`.
3. Controller maps `Success` → 204; `InvalidToken` → 401 `INVALID_EMAIL_CHANGE_TOKEN`.

### 3.4 Cross-feature: password-reset cancels pending email-change

Edit `PasswordResetService.ConfirmAsync`. Inside the existing per-user semaphore, after the `UserSessions` `ExecuteUpdateAsync` revocation (line 315) and before `UpdateSecurityStampAsync` (line 318):

```csharp
var hadPending = await _db.EmailChangeTokens
    .AnyAsync(t => t.UserId == user.Id && t.ConsumedAt == null, ct);
if (hadPending)
{
    await _db.EmailChangeTokens
        .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

    try
    {
        await _email.SendAsync(BuildEmailChangeCancelledByPasswordResetEmail(user.Email!), ct);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to send email-change-cancelled-by-password-reset email.");
    }
}
```

`PasswordResetService` already injects `AppDbContext` and `IEmailService` — no constructor change. `BuildEmailChangeCancelledByPasswordResetEmail` is a new private static helper inside `PasswordResetService`.

Reasoning: a password reset is itself a recovery/compromise signal. If an attacker initiated `/email-change/request` before being password-reset out, leaving the verify token live for up to 30 minutes lets them complete the takeover after the legitimate user resets. Atomic cancellation closes that window.

## 4. Edge cases

### 4.1 Token-state edges

- Expired token (>30 min for VerifyNew, >7 days for RevokeOld) → 401 `INVALID_EMAIL_CHANGE_TOKEN`.
- Already-consumed token → 401.
- Superseded token (request issued, then a second request issued before the first was used) → 401.
- Token from a deleted user → `FindByIdAsync` returns null → 401.
- Replay within window → blocked because `ConsumedAt` is set synchronously in the same `ExecuteUpdateAsync` as the Identity update.
- Malformed token string → `Verify` fails on every candidate → 401. Constant-time still applies (dummy verify if zero candidates).

### 4.2 Email-collision edges

- `newEmail` equals current `user.Email` (case-insensitive against `NormalizedEmail`) → 422 `EMAIL_UNCHANGED` at request time.
- `newEmail` registered to another user at request time → 422 `EMAIL_ALREADY_IN_USE` at request time.
- `newEmail` grabbed by another user *between* request and confirm → 422 `EMAIL_ALREADY_IN_USE` at confirm time. **Token NOT consumed** — caller can `/revoke` to clean up.
- `newEmail` equals the *normalized* form of another user's email (e.g. uppercase variation) → Identity normalizer catches it via the unique index.
- `RequireUniqueEmail = true` (Program.cs:79) is enforced on `SetEmailAsync` via `UserManager`'s pre-check; we additionally re-check with `FindByEmailAsync` for clearer error mapping.

### 4.3 Reauth-gate edges

- `/request` with valid session but stale `LastReauthAt` claim → 401 `REAUTH_REQUIRED` from `RecentAuthMiddlewareResultHandler`. No token rows written.
- `/request` with no session at all → 401 from `[Authorize]` (default policy).
- `/confirm` and `/revoke` are `[AllowAnonymous]`; reauth gate doesn't apply. The token IS the auth.

### 4.4 Session-revocation edges

- `/confirm` revokes ALL `UserSession` rows for the user (including any persistent-cookie row) and regens `SecurityStamp`. Any in-flight Identity cookie is invalidated within 5 min via `SecurityStampValidator`.
- `/revoke` does NOT revoke sessions (it's a cancel, not a security event for the legitimate user).
- Failed `/confirm` (any `InvalidToken` / `EmailAlreadyInUse` outcome) → no sessions touched. Negative-assertion test pins this.

### 4.5 Lockout edges (negative)

- `/confirm` does NOT call `ResetAccessFailedCountAsync` and does NOT call `SetLockoutEndDateAsync(user, null)`. **Explicit divergence from password-reset.** Reasoning: email proof is not equivalent to password recovery. A user whose lockout fires from password guessing should not be unlocked because they happened to confirm an email change.
- Negative-assertion test pins both `LockoutEnd` and `AccessFailedCount` unchanged after a successful `/confirm`.

### 4.6 Concurrency edges

- Two concurrent `/confirm` with the same raw token → exactly one returns 204, the other 401. Per-user semaphore + atomic `ExecuteUpdateAsync` enforce.
- Two concurrent `/request` for the same user → only the winner's pair active; the other's pair is consumed by the second `/request`'s supersede sweep.
- Concurrent `/request` + `/confirm` against a partially-consumed state — invariants: no duplicate active rows; sibling pairing intact.

### 4.7 Cross-feature edges

- Pending email-change + successful `/password-reset/confirm` → both `EmailChangeToken` rows for that user have `ConsumedAt != null`. Cancellation email sent to old address (the address-of-record at password-reset time).
- Subsequent `/email-change/confirm` after password-reset cancellation → 401 `INVALID_EMAIL_CHANGE_TOKEN`.
- Subsequent `/email-change/revoke` after password-reset cancellation → 401 `INVALID_EMAIL_CHANGE_TOKEN`.

### 4.8 Email-delivery edges

- `IEmailService.SendAsync` throws → caught and logged; DB writes are already committed; user can request again.
- Production with no `IEmailService` registered → DI throws at startup. Intentional, blocks production deployment until Stage 8.

## 5. Tests

All under `ProjectCeres.Tests/Integration/Authentication/`. Reuse `AuthTestWebApplicationFactory`, `AuthTestFixture.RegisterUserAsync`, `AuthTestFixture.PostJsonWithCsrfAsync`. Fresh `LastReauthAt` requires hitting `POST /api/auth/reauth` after login (mirror `ReauthCrossFeatureTests`).

A new test-only `CapturingEmailService` lives at `ProjectCeres.Tests/Integration/CapturingEmailService.cs` (test fixture only). Registered via `b.ConfigureTestServices(s => { s.RemoveAll<IEmailService>(); s.AddSingleton<IEmailService>(new CapturingEmailService(captured)); })` to capture and assert sends.

### 5.1 Test discipline

- No retry-loops, no Skip on intermittent failures — diagnose and fix.
- `MockBehavior.Strict` on any mock; default to `CapturingEmailService` over Moq for `IEmailService`.
- Every async test wraps in `CancellationTokenSource(TimeSpan.FromSeconds(30))` and propagates the token.
- Time-based tests insert rows with adjusted `ExpiresAt` (no `IClock` abstraction; out of scope per plan policy).
- Rate-limit tests live in xUnit collection `"RateLimitTests"` to isolate process-shared bucket state.

### 5.2 Test list (40 tests)

#### `EmailChangeRequestTests.cs` (9 tests)

1. `Request_happy_path_returns_202_and_persists_two_token_rows_and_sends_two_emails`.
2. `Request_without_recent_reauth_returns_401_REAUTH_REQUIRED_and_writes_no_rows` (negative).
3. `Request_with_newEmail_already_registered_returns_422_EMAIL_ALREADY_IN_USE_and_writes_no_rows_and_sends_no_emails` (negative).
4. `Request_with_newEmail_equal_to_current_returns_422_EMAIL_UNCHANGED_and_writes_no_rows` (negative; case-insensitive against `NormalizedEmail`).
5. `Second_request_supersedes_first_marking_first_two_rows_consumed_and_inserts_two_fresh_rows`.
6. `Request_eleventh_call_in_one_minute_from_same_IP_returns_429_via_AuthLoginByIp`.
7. `Request_sixth_call_against_same_newEmail_within_one_hour_returns_429_RATE_LIMITED_with_RetryAfter`.
8. `Request_emits_verify_email_to_new_and_revoke_email_to_old_with_distinct_tokens`.
9. `Request_does_NOT_bypass_reauth_with_valid_session_cookie` (negative).

#### `EmailChangeConfirmTests.cs` (9 tests)

10. `Confirm_happy_path_updates_email_and_normalized_email_and_username_and_normalized_username_and_emailconfirmed_true`.
11. `Confirm_revokes_all_user_sessions_and_regenerates_security_stamp`.
12. `Confirm_consumes_sibling_RevokeOld_row`.
13. `Confirm_with_invalid_token_returns_401_INVALID_EMAIL_CHANGE_TOKEN`.
14. `Confirm_with_expired_token_returns_401`.
15. `Confirm_with_already_consumed_token_returns_401`.
16. `Confirm_with_collision_grabbed_between_request_and_confirm_returns_422_EMAIL_ALREADY_IN_USE_and_does_NOT_consume_token` (negative).
17. `Confirm_does_NOT_clear_lockout` (negative — `LockoutEnd` and `AccessFailedCount` unchanged).
18. `Confirm_sends_change_confirmed_email_to_new_address`.

#### `EmailChangeRevokeTests.cs` (7 tests)

19. `Revoke_happy_path_leaves_user_Email_UNCHANGED` (**critical**).
20. `Revoke_consumes_sibling_VerifyNew_row`.
21. `Revoke_does_NOT_revoke_sessions_and_does_NOT_regenerate_security_stamp` (negative).
22. `Revoke_with_invalid_token_returns_401`.
23. `Revoke_with_expired_token_returns_401` (8 days past).
24. `Revoke_with_already_consumed_token_returns_401`.
25. `Revoke_sends_email_to_OLD_address_only`.

#### `EmailChangeConcurrencyTests.cs` (3 tests)

26. `Two_concurrent_confirms_with_same_token_only_one_returns_204_other_401`.
27. `Two_concurrent_requests_for_same_user_one_pair_supersedes_the_other`.
28. `Concurrent_request_and_confirm_against_partially_consumed_state_does_not_corrupt_rows`.

#### `EmailChangeCrossFeatureTests.cs` (4 tests)

29. `Pending_email_change_plus_successful_password_reset_consumes_both_email_change_rows`.
30. `Cancellation_email_fires_to_OLD_address_when_password_reset_consumes_pending_email_change`.
31. `After_password_reset_consumes_pending_email_change_subsequent_email_change_confirm_returns_401`.
32. `After_password_reset_consumes_pending_email_change_subsequent_email_change_revoke_returns_401`.

#### `EmailChangeRateLimitTests.cs` (4 tests, in `RateLimitTests` collection)

33. `Eleventh_confirm_from_same_IP_in_one_minute_returns_429`.
34. `Eleventh_revoke_from_same_IP_in_one_minute_returns_429`.
35. `Sixth_request_against_same_newEmail_within_one_hour_returns_429_with_RetryAfter`.
36. `Per_email_rate_limit_is_keyed_by_newEmail_not_oldEmail` — two requesting users targeting same `newEmail` share bucket; same user targeting two different `newEmail`s uses separate buckets.

#### `ArchitectureTests.cs` additions (4 tests)

37. `EmailChangeController_has_correct_attribute_matrix` — `RequestChange`: `[Authorize] + [RequireRecentAuth] + [HttpPost("request")]`; `ConfirmChange`/`RevokeChange`: `[AllowAnonymous] + [EnableRateLimiting(AuthLoginByIp)] + [HttpPost(...)]`.
38. `EmailChangeService_methods_take_CancellationToken` — last param of all three public methods is `CancellationToken`.
39. `EmailChangeController_does_not_call_PasswordHasher_directly` — no field of type `Argon2idPasswordHasher`.
40. `EmailChangeController_does_not_have_class_level_AllowAnonymous` — explicit named test for ergonomics (covered by existing global rule).

### 5.3 Negative-assertion bullets (per `feedback_test_edge_cases_as_ship_gate`)

These are easy to forget; each lives inside the test files above:

- `Revoke_happy_path_does_NOT_update_AspNetUsers_Email`.
- `Confirm_does_NOT_clear_lockout`.
- `Request_does_NOT_bypass_reauth_with_valid_session_cookie`.
- `Confirm_with_collision_does_NOT_consume_VerifyNew_token`.
- `Revoke_does_NOT_revoke_sessions_and_does_NOT_regenerate_security_stamp`.
- `Request_with_newEmail_already_registered_does_NOT_send_emails`.

## 6. Sequencing

Implementation order (matches plan sub-stage checkpointing):

1. **6.12.1 Spec + entity + migration** — this file, `EmailChangeToken.cs`, `EmailChangeTokenPurpose` enum, `AppDbContext` config, `AddEmailChangeTokens` migration. `dotnet build` green.
2. **6.12.2 Token generator + service skeleton + `/request` happy path** — `EmailChangeTokenGenerator.cs`, `EmailChangeService.RequestAsync`, controller stub for `/request`, first red→green test.
3. **6.12.3 `/request` ship-gate** — tests #1-9.
4. **6.12.4 `/confirm` + ship-gate** — `EmailChangeConfirmOutcome`, `ConfirmAsync`, `ConfirmChange`, tests #10-18.
5. **6.12.5 `/revoke` + ship-gate** — `RevokeAsync`, `RevokeChange`, tests #19-25.
6. **6.12.6 Concurrency + rate-limit tests** — tests #26-36.
7. **6.12.7 Cross-feature wiring** — edit `PasswordResetService.ConfirmAsync`, tests #29-32.
8. **6.12.8 Architecture tests + docs sync** — tests #37-40, security-model.md / api-contract.md / roadmap-phase-three.md / planning-phase3.md / planning-resolved.md / changelog updates, `sync-docs` skill, full `dotnet test` green.

## 7. Files

### Created

- `ProjectCeres/Models/EmailChangeToken.cs`
- `ProjectCeres/Common/Authentication/EmailChangeService.cs`
- `ProjectCeres/Common/Authentication/EmailChangeTokenGenerator.cs`
- `ProjectCeres/Common/Authentication/EmailChangeOutcomes.cs`
- `ProjectCeres/Controllers/Api/EmailChangeController.cs`
- `ProjectCeres/ViewModels/Auth/EmailChangeRequest.cs`
- `ProjectCeres/ViewModels/Auth/EmailChangeConfirmRequest.cs`
- `ProjectCeres/ViewModels/Auth/EmailChangeRevokeRequest.cs`
- `ProjectCeres/Migrations/<timestamp>_AddEmailChangeTokens.cs`
- `ProjectCeres.Tests/Integration/CapturingEmailService.cs`
- Tests under `ProjectCeres.Tests/Integration/Authentication/`:
  - `EmailChangeRequestTests.cs`
  - `EmailChangeConfirmTests.cs`
  - `EmailChangeRevokeTests.cs`
  - `EmailChangeConcurrencyTests.cs`
  - `EmailChangeCrossFeatureTests.cs`
  - `EmailChangeRateLimitTests.cs`
  - Architecture-test additions land in existing `ArchitectureTests.cs`.

### Modified

- `ProjectCeres/Data/AppDbContext.cs` — add `DbSet<EmailChangeToken>` + `ConfigureEmailChangeEntities` private method.
- `ProjectCeres/Program.cs` — register `EmailChangeService`, `EmailChangeTokenGenerator`.
- `ProjectCeres/Common/Authentication/PasswordResetService.cs` — cross-feature: cancel pending email-change on confirm.
- `docs/api-contract.md` — add three error codes; add three auth-endpoint rows.
- `docs/security-model.md` — append Stage 6.12 status banner; add concurrency / rate-limit / cross-feature bullets.
- `docs/roadmap-phase-three.md` — Stage 6.12 banner blockquote; checkbox flips.
- `docs/planning-phase3.md` — Stage 6.12 entry near password-reset entry.
- `docs/planning-resolved.md` — log three policy decisions.
- `CHANGELOG.md` — `[Unreleased]` "Added — `POST /api/auth/email-change/request|confirm|revoke` (Stage 6.12)".

## 8. What this sub-stage does NOT cover

- UI (`/settings/email`) — Stage 9 / Stage 12.
- `AuditLog` writes — Stage 6.14.
- Real email provider — Stage 8.
- EN/ES `.resx` templates — Stage 8.
- Granular cool-down on re-issuing tokens within the same window — service-side rate limit covers it.
- Email change for SSO/external users — no SSO in Phase 3.

## 9. Production-deployment guard

Production has NO `IEmailService` registered. DI resolution throws at startup — this is intentional and blocks Phase 3 production deployment until Stage 8 wires the real provider. The Stage 6c.1 password-reset flow established this pattern; Stage 6.12 inherits it.
