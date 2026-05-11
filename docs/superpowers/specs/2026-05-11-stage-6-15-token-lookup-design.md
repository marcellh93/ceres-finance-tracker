# Stage 6.15 — O(1) token verify via HMAC `TokenLookup` column

**Date:** 2026-05-11
**Stage:** Phase 3, Stage 6 (Identity infrastructure, follow-up to 6c.1 + 6.12)
**Sub-stage:** 6.15 (token-verify amplification DoS mitigation)
**Author:** brainstormed with user, approved 2026-05-11
**Plan:** TBD when this is scheduled for execution
**Status:** ❌ Pending — sequenced after Stage 6.14 (audit log) and the lockout self-service unlock endpoint per the Stage 6c sequencing decision (2026-05-11)

## 1. Context

`PasswordResetService.ConfirmAsync`, `EmailChangeService.ConfirmAsync`, and `EmailChangeService.RevokeAsync` each load every unconsumed unexpired row from their token table, then run `Argon2idPasswordHasher.VerifyHashedPassword` against each one until a match is found.

```csharp
// PasswordResetService.cs:204-223 — the existing candidate-loop pattern
var candidates = await _db.PasswordResetTokens
    .Where(t => t.ConsumedAt == null && t.ExpiresAt > now)
    .ToListAsync(ct);

PasswordResetToken? match = null;
foreach (var candidate in candidates)
{
    if (_tokens.Verify(rawToken, candidate.TokenHash))  // ← Argon2id per candidate
    {
        match = candidate;
        break;
    }
}
```

At OWASP-minimum Argon2id parameters (m=19456, t=2, p=1) each `Verify` costs ~100-200ms standalone, 5-19s under integration-test load. With N active tokens, every legitimate `/confirm` or `/revoke` request runs ~N Argon2id verifies before either finding a match or running the dummy hash. A few hundred active tokens turn the verify path into multiple seconds of CPU.

This is a real DoS amplifier. An attacker who can sustain `/password-reset/request` or `/email-change/request` (which they can under the existing per-IP and per-email rate limits, spread across emails and IPs) can grow N indefinitely. The `EmailChangeToken` `RevokeOld` rows are particularly bad — they live 7 days, so accumulation is most painful there.

Discovered 2026-05-11 during the Stage 6.12 ship-gate run; test infrastructure was mitigated at the same time by adding token-table cleanup to every PasswordReset/EmailChange test class (`ProjectCeres.Tests/Integration/Authentication/AuthTestTokenCleanup.cs`). Production code unchanged at that time. This spec is the production fix.

> **⚠️ Important — test green-light does NOT mean production is safe.**
>
> Today's test suite passes because `AuthTestTokenCleanup.DeleteAllTestTokensAsync`
> empties `PasswordResetTokens` and `EmailChangeTokens` after every test class
> runs. **Production has no such cleanup.** Active tokens accumulate naturally in
> dev/staging/production until they're consumed or expire (15 min for
> password-reset, 30 min for email-change verify, **7 days for email-change
> revoke**). The DoS-amplification bug described in this spec exists in
> dev/staging/production right now even though every relevant test is green.
>
> The test-side cleanup was added to unblock the Stage 6.12 ship-gate without
> shipping an incomplete production fix the same day. It is technical debt, not
> a solution. Per `feedback_test_edge_cases_as_ship_gate` ("don't manipulate
> tests to get a green light and leave the codebase with the security gap"),
> Stage 6.15's ship-gate explicitly **deletes** the `AuthTestTokenCleanup`
> helper and the `IAsyncLifetime.DisposeAsync` hooks that call it. Tests must
> pass under accumulated token load — that's what the new DoS-amplification
> regression tests (§ 8.1) verify.
>
> Until 6.15 ships, anyone reviewing the green test runs should know the
> tests are passing **despite** the production bug, not because of its absence.

## 2. Scope

In scope:

- Add a `TokenLookup byte[]` column to `PasswordResetToken` and `EmailChangeToken` populated at insert with `HMAC-SHA256(secret, rawToken)` (full 32-byte output).
- Add a unique index on `TokenLookup` for each table.
- New `TokenLookupHasher` service injected into both services, computing `HMAC-SHA256(secret, rawToken)`.
- New `Authentication:TokenLookupSecret` config value with the same secret-store + rotation conventions as `USER_REF_SECRET` (security-model.md § 793-811). **NOT** the same secret as `USER_REF_SECRET` — different purpose, independent rotation cadence.
- Refactor `PasswordResetService.ConfirmAsync`, `EmailChangeService.ConfirmAsync`, and `EmailChangeService.RevokeAsync` from the candidate-loop pattern to O(1) lookup-then-verify.
- Two EF migrations: `AddTokenLookupToPasswordResetTokens`, `AddTokenLookupToEmailChangeTokens`. Both backfill existing rows by HMACing **the existing TokenHash** (not the raw token — we don't have it) as a placeholder so the unique index can be added; **all existing rows are then marked `ConsumedAt = now` so they cannot accidentally match a verify call** with a real `TokenLookup`. The migration's safe interpretation: existing pre-6.15 tokens are invalidated by the upgrade. Acceptable because pre-launch the affected populations are dev/test data.
- Architecture test: verify the candidate-loop pattern is gone (assert against the service source that there's no `foreach (var candidate in candidates)` loop after a `ToListAsync`).
- DoS-amplification regression test: insert N=200 dummy rows directly into the DB, fire one `/email-change/revoke` call, assert it completes in < 1s.

Out of scope (deferred):

- HKDF-derived purpose-scoped secrets (e.g. `password-reset-lookup` vs `email-change-lookup`). The spec uses one shared `TokenLookupSecret` and discriminates by table; HKDF would only matter under cryptographic-rotation-isolation requirements we don't have.
- Backward-compatibility window where old pre-6.15 rows still validate via the legacy candidate loop. Out of scope because (a) pre-launch we control the test data, (b) any post-launch rollout pre-Phase-3-public-launch can drain the existing token tables overnight (~7 days for EmailChange RevokeOld; ~15 min for PasswordReset).
- The same O(1) lookup pattern applied to `UserMfaBackupCode`. Backup codes are different — there are at most 10 active per user, so the candidate-loop cost is bounded. No DoS amplification. Out of scope.
- Per-tenant payload encryption (Phase 4+).

## 3. Decisions

### 3.1 Why a config-driven shared secret, not `IDataProtectionProvider`

- `IDataProtectionProvider` is not used anywhere in production code yet — it'd be a new infrastructure dependency.
- The `__Host-Persist` cookie already validates this approach: it uses `Argon2id` over a config-stored secret, not `IDataProtectionProvider`, and that decision was made deliberately in Stage 6b.3 (`UserSession.PersistentTokenHash` pattern).
- HMAC-SHA256 over a 32-byte secret has the same security profile as a `IDataProtector` purpose-key derivation for this use case — both are server-side keyed hashes. The config-driven path is simpler to operate.

### 3.2 Why NOT the same secret as `USER_REF_SECRET`

`USER_REF_SECRET` (Stage 15) pseudonymises every user-owned data row's foreign-key column. Rotating it requires rewriting every `UserRef` column in the database — a full data migration. The lookup secret rotates independently: changing it invalidates every active password-reset and email-change token (users in the middle of recovery would need to restart), which is a much lighter operation.

Keeping them separate means:

- Stage 15's `USER_REF_SECRET` rotation doesn't force any auth-token invalidation.
- A breach of one secret doesn't compromise the other.
- The lookup secret can rotate frequently if needed; `USER_REF_SECRET` rotates only on suspected exposure.

Both secrets share infrastructure: the same hosting-platform secret store, the same security-model.md § Secrets Rotation Procedures section.

### 3.3 Migration strategy for existing rows

When the migration runs, existing `PasswordResetToken` and `EmailChangeToken` rows have a `TokenHash` (Argon2id of the raw token) but **no raw token** — we can't compute the real `TokenLookup`. Strategy: backfill a synthetic placeholder so the unique index can be added, then immediately mark every existing row `ConsumedAt = NOW()` so it can never match a real verify call.

```csharp
migrationBuilder.Sql(@"
    UPDATE ""PasswordResetTokens""
    SET ""ConsumedAt"" = NOW(),
        ""TokenLookup"" = decode(md5(""TokenHash"" || ""Id""::text), 'hex')
    WHERE ""ConsumedAt"" IS NULL;
");
```

**Why no transitional dual-validation code is required.** Stage 6.15 must ship before Phase 3 public launch (it's part of the Stage 6 verification gate per `roadmap-phase-three.md` master pre-launch checklist, "All Stage 6 verification items green"). At ship time the affected populations are dev/test data only — invalidating in-flight tokens has zero real-user impact because there are no real users yet. Once Phase 3 launches, every token issued is already on the new schema; no legacy tokens exist.

If Stage 6.15 ever needed to land mid-flight in a live system (which the sequencing rule forbids), the design would require a dual-validation transitional window because the 7-day `EmailChangeToken.RevokeOld` window is too long to invalidate silently — a user clicking a "this wasn't me" revoke link must never silently fail. That scenario is structurally prevented by the Phase 3 verification gate and so isn't designed for here.

The migration's `Up` writes both column-add and `ConsumedAt`-stamp in one `MigrationBuilder` block. The `Down` drops the column (best-effort — a downgrade after rollout would lose audit fidelity for any tokens that legitimately consumed between Up and Down, which is acceptable for a one-way Phase-3 migration).

### 3.4 Constant-time defence stays intact

The current dummy-hash defence pays one Argon2id-cost when the lookup misses. The refactored path keeps the dummy hash but moves it after the O(1) lookup:

```csharp
var lookup = _hasher.ComputeLookup(rawToken);
var match = await _db.PasswordResetTokens
    .Where(t => t.TokenLookup == lookup && t.ConsumedAt == null && t.ExpiresAt > now)
    .FirstOrDefaultAsync(ct);

if (match is null)
{
    _argon.RunDummyHash();                       // ← constant-time floor
    return new InvalidToken();
}

if (!_tokens.Verify(rawToken, match.TokenHash))  // ← real Argon2id verify
{
    return new InvalidToken();                   // (no dummy needed; the verify itself was real)
}
```

Net cost per call: O(1) HMAC + O(1) Argon2id verify ≈ 0.1ms + 200ms. Independent of the size of the token table.

## 4. Schema

### 4.1 `PasswordResetToken` (`ProjectCeres/Models/PasswordResetToken.cs`)

```csharp
public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] TokenLookup { get; set; } = Array.Empty<byte>();   // ← new (Stage 6.15)
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? MfaVerifiedAt { get; set; }
}
```

`AppDbContext` configuration:

```csharp
modelBuilder.Entity<PasswordResetToken>(b =>
{
    b.HasKey(e => e.Id);
    b.HasIndex(e => new { e.UserId, e.ConsumedAt });
    b.HasIndex(e => e.ExpiresAt);
    b.HasIndex(e => e.TokenLookup).IsUnique();                       // ← new (Stage 6.15)
    b.Property(e => e.TokenLookup).HasMaxLength(32);                 // ← new (Stage 6.15)
    b.Property(e => e.TokenHash).HasMaxLength(512);
});
```

### 4.2 `EmailChangeToken` (`ProjectCeres/Models/EmailChangeToken.cs`)

Same shape — add `TokenLookup byte[]` with `HasMaxLength(32)` and a unique index. The unique index does NOT include `Purpose` because a single raw token must never match against more than one row regardless of purpose.

### 4.3 Migrations

Two migrations, both follow this template:

```csharp
public partial class AddTokenLookupToPasswordResetTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "TokenLookup",
            table: "PasswordResetTokens",
            type: "bytea",
            maxLength: 32,
            nullable: false,
            defaultValue: new byte[0]);

        // Invalidate existing rows so the unique index can be added.
        // Pre-6.15 raw tokens cannot be recomputed; mark all rows consumed.
        migrationBuilder.Sql(@"
            UPDATE ""PasswordResetTokens""
            SET ""ConsumedAt"" = NOW(),
                ""TokenLookup"" = decode(md5(""TokenHash"" || ""Id""::text), 'hex')
            WHERE ""ConsumedAt"" IS NULL;
        ");

        migrationBuilder.CreateIndex(
            name: "IX_PasswordResetTokens_TokenLookup",
            table: "PasswordResetTokens",
            column: "TokenLookup",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { /* drop column + index */ }
}
```

The `md5(TokenHash || Id::text)` synthetic placeholder is purely to satisfy the unique index — these tokens are consumed in the same statement so they can never match a real verify call.

## 5. New service: `TokenLookupHasher`

`ProjectCeres/Common/Authentication/TokenLookupHasher.cs`:

```csharp
public sealed class TokenLookupHasher
{
    private readonly byte[] _key;

    public TokenLookupHasher(IOptions<TokenLookupOptions> options)
    {
        var secret = options.Value.Secret
            ?? throw new InvalidOperationException(
                "Authentication:TokenLookupSecret is required.");
        // Base64-decoded 32-byte (256-bit) secret. Rotation requires
        // invalidating all pending password-reset and email-change tokens;
        // see security-model.md § Secrets Rotation Procedures.
        _key = Convert.FromBase64String(secret);
        if (_key.Length < 32)
            throw new InvalidOperationException(
                "Authentication:TokenLookupSecret must decode to >= 32 bytes.");
    }

    public byte[] ComputeLookup(string rawToken)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(rawToken));
    }
}

public sealed class TokenLookupOptions
{
    public string? Secret { get; set; }
}
```

DI registration in `Program.cs` next to the Argon2id options:

```csharp
builder.Services.Configure<TokenLookupOptions>(
    builder.Configuration.GetSection("Authentication:TokenLookupSecret"));
builder.Services.AddSingleton<TokenLookupHasher>();
```

`appsettings.json` schema:

```json
"Authentication": {
  "TokenLookupSecret": {
    "Secret": "<base64-of-32+-random-bytes>"
  }
}
```

Local development: `dotnet user-secrets`. Production: hosting-platform secret store, same convention as `USER_REF_SECRET`.

Startup behaviour: missing secret in any environment → `InvalidOperationException` at first DI resolution. **Deliberately fail loudly** to prevent shipping without the secret configured.

## 6. Service refactor

### 6.1 `PasswordResetService.ConfirmAsync`

Replace the candidate-loop block (currently lines ~204-223) with:

```csharp
var lookup = _lookupHasher.ComputeLookup(rawToken);
var match = await _db.PasswordResetTokens
    .Where(t => t.TokenLookup == lookup
             && t.ConsumedAt == null
             && t.ExpiresAt > now)
    .FirstOrDefaultAsync(ct);

if (match is null)
{
    _argon.RunDummyHash();
    return new PasswordResetConfirmOutcome.InvalidToken();
}

if (!_tokens.Verify(rawToken, match.TokenHash))
{
    return new PasswordResetConfirmOutcome.InvalidToken();
}
```

The rest of `ConfirmAsync` (MFA branch, password write, session revoke, etc.) is untouched.

Insert path (in `RequestAsync` at line ~108): compute and store `TokenLookup` alongside `TokenHash`:

```csharp
rawToken = _tokens.Generate();
var hash = _tokens.Hash(rawToken);
var lookup = _lookupHasher.ComputeLookup(rawToken);

_db.PasswordResetTokens.Add(new PasswordResetToken
{
    Id = Guid.NewGuid(),
    UserId = user.Id,
    TokenLookup = lookup,            // ← new (Stage 6.15)
    TokenHash = hash,
    CreatedAt = now,
    ExpiresAt = now + TokenLifetime,
    ConsumedAt = null,
    MfaVerifiedAt = null,
});
```

Constructor adds `TokenLookupHasher lookupHasher` parameter.

### 6.2 `EmailChangeService.ConfirmAsync` and `EmailChangeService.RevokeAsync`

Same pattern, one variant: the email-change service has two purposes (VerifyNew + RevokeOld) and the verify code must filter by purpose. The `Where` clause becomes:

```csharp
// ConfirmAsync: looking for VerifyNew tokens
var match = await _db.EmailChangeTokens
    .Where(t => t.TokenLookup == lookup
             && t.Purpose == EmailChangeTokenPurpose.VerifyNew
             && t.ConsumedAt == null
             && t.ExpiresAt > now)
    .FirstOrDefaultAsync(ct);

// RevokeAsync: looking for RevokeOld tokens
var match = await _db.EmailChangeTokens
    .Where(t => t.TokenLookup == lookup
             && t.Purpose == EmailChangeTokenPurpose.RevokeOld
             && t.ConsumedAt == null
             && t.ExpiresAt > now)
    .FirstOrDefaultAsync(ct);
```

The unique index on `TokenLookup` alone (no `Purpose` column) is correct: a single raw token must never collide across purposes, so the index enforces that AND ensures fast lookup. The `.Where(... && t.Purpose == ...)` is a defence-in-depth check that the matched row's purpose is what we expected — a defensive guard, not a correctness requirement.

Insert path in `RequestAsync` (line ~110-130): both VerifyNew and RevokeOld rows get distinct `rawToken`s, so they get distinct `TokenLookup`s. No collision risk.

### 6.3 Constructor injection

Both services add a `TokenLookupHasher` constructor parameter. DI resolves via the new singleton registration.

## 7. Error returns (unchanged)

- `PasswordResetController` continues to map `InvalidToken` → `401 INVALID_RESET_TOKEN`.
- `EmailChangeController` continues to map `InvalidToken` → `401 INVALID_EMAIL_CHANGE_TOKEN`.
- The envelope shape `{ error: { code, message } }` is unchanged.
- `api-contract.md` § Canonical error codes does NOT change (the codes remain valid for the same semantic class of failure).

## 8. Tests

### 8.1 New tests

- **`TokenLookupHasherTests` (unit)** — `ComputeLookup` produces deterministic 32-byte output for the same input; differs for one-bit-changed input; throws on missing secret.
- **`PasswordResetVerifyDosAmplificationTests` (integration)** — insert N=200 dummy `PasswordResetToken` rows directly into the DB, then fire `/password-reset/confirm` with an unrelated token. Assert: completes in < 1 second (vs the pre-6.15 baseline of ~20-40 seconds). Pins the regression.
- **`EmailChangeVerifyDosAmplificationTests` (integration)** — same for `/email-change/confirm` and `/email-change/revoke`. Two variants because each goes through a different `Where(Purpose == ...)` filter.
- **`PasswordResetServiceArchitectureTest`** — reflect over `PasswordResetService.ConfirmAsync` (via Roslyn or source-text grep) and assert no `foreach` loop over `_db.PasswordResetTokens.ToListAsync()`. Same for `EmailChangeService.ConfirmAsync` and `EmailChangeService.RevokeAsync`.

### 8.2 Tests that need updating

- `PasswordResetRequestTests.Request_with_known_email_issues_token_and_sends_email` (line ~49-60) asserts row fields after `RequestAsync`. Update to also assert `token.TokenLookup` is non-empty and length 32.
- `EmailChangeRequestTests.Request_happy_path_returns_202_and_persists_two_token_rows_and_sends_two_emails` (line ~74-103) asserts row fields. Add assertions that both VerifyNew and RevokeOld rows have `TokenLookup` set and that the two values differ.

### 8.3 Tests that pass unchanged (regression coverage)

- All existing happy-path, expiry, supersession, concurrency, rate-limit, cross-feature, and architecture tests under `PasswordReset*Tests.cs` and `EmailChange*Tests.cs` MUST continue to pass without code change. The refactor is a pure performance improvement of the verify path — semantics unchanged.

### 8.4 Test-infrastructure debt: REMOVE the `AuthTestTokenCleanup` cleanup

**This is the ship-gate enforcement of § 1's "test green-light ≠ production safe" warning.**

The current test suite passes because every PasswordReset and EmailChange test class implements `IAsyncLifetime.DisposeAsync` and calls `AuthTestTokenCleanup.DeleteAllTestTokensAsync` after every test class runs, which deletes every `PasswordResetTokens` and `EmailChangeTokens` row owned by `@example.com` users. **That cleanup masks the DoS amplification.** It must be **removed** as part of Stage 6.15:

- Delete `ProjectCeres.Tests/Integration/Authentication/AuthTestTokenCleanup.cs`.
- Strip `IAsyncLifetime` (`InitializeAsync`/`DisposeAsync` lines) from the following 12 test classes:
  - `EmailChangeRequestTests`, `EmailChangeConfirmTests`, `EmailChangeRevokeTests`, `EmailChangeConcurrencyTests`, `EmailChangeCrossFeatureTests`, `EmailChangeRateLimitTests`
  - `PasswordResetRequestTests`, `PasswordResetConcurrencyTests`, `PasswordResetConfirmMfaTests`, `PasswordResetConfirmNoMfaTests`, `PasswordResetSessionRevocationTests`, `PasswordResetRateLimitTests`
- Run the full `ProjectCeres.Tests.Integration.Authentication` suite **without the cleanup hooks**. All 243 tests must remain green. If any go red, that's a real regression the candidate-loop was hiding — fix it.
- After removal, the green test suite genuinely reflects that production is safe under accumulated token load.

The DoS-amplification regression tests (§ 8.1) explicitly insert N=200 dummy rows to verify the O(1) property holds — those tests guarantee that future regressions which reintroduce a candidate-loop pattern fail immediately, even without the cleanup hooks. They replace the safety the cleanup was providing.

## 9. Documentation updates landing with 6.15

- `docs/api-contract.md` — no change (error codes preserved).
- `docs/security-model.md` § Password Reset / § Email Address Change — add a "Verify path" subsection explaining the O(1) lookup pattern and the `TokenLookupSecret` rotation procedure (cross-reference § Secrets Rotation Procedures).
- `docs/security-model.md` § Secrets — add `TokenLookupSecret` to the list at line ~609 alongside `USER_REF_SECRET`.
- `docs/roadmap-phase-three.md` — Stage 6.15 banner blockquote near the other Stage 6 banners; flip the 6.15 verification checklist items below.
- `docs/planning-phase3.md` — move the existing "Stage 6.15 — Argon2id-O(N) DoS vector" entry to past-tense and link to `planning-resolved.md`.
- `docs/planning-resolved.md` — add the Stage 6.15 resolution entry mirroring 6c.1 / 6c.2 / 6.12 prior entries.
- `CHANGELOG.md` — `[Unreleased]` entry "Fixed — Argon2id-amplification DoS vector on `/password-reset/confirm`, `/email-change/confirm`, `/email-change/revoke` via indexed `TokenLookup` column. Verify cost now O(1) regardless of token table size."

## 10. Sequencing within Stage 6 close-out

Per the Stage 6c sequencing decision (2026-05-11):

1. Stage 6.14 — audit log table + writer service.
2. Lockout self-service unlock signed-token endpoint (closes the last Stage 6.10 gap).
3. **Stage 6.15 (this spec).**
4. `AuthMfaByUser` rate-limit partition fix (follow-up from Stage 6c.2).
5. Stage 6 close-out documentation: auth-flow diagrams in `security-model.md`.

Stage 6.15 lands as a single ship-gate green commit. No partial rollout, no feature flag — the migration's `ConsumedAt`-stamp-on-existing-rows handles the schema cutover atomically.

## 11. Estimated effort

Half-day:

- Entity + DbContext + migration × 2: ~1h.
- `TokenLookupHasher` + DI wiring: ~30min.
- Service refactor × 3 verify paths + × 2 insert paths: ~1.5h.
- Test additions and updates: ~1.5h.
- Doc sync: ~30min.

## 12. What 6.15 does NOT cover

- HKDF-derived purpose-scoped lookup keys.
- Backward-compatibility window for legacy candidate-loop fallback.
- `UserMfaBackupCode` (bounded at 10/user; no DoS amplification).
- Per-tenant payload encryption (Phase 4+).
- Stage 15's `USER_REF_SECRET` rollout (parallel work item, shares secret-store infrastructure but is independent).
