# Stage 9.1.5.a — `LockoutUnlockToken.TokenLookup` (extend Stage 6.15 pattern)

**Status:** Spec — pending user review, then writing-plans skill.
**Phase:** Phase 3, Stage 9.1.5 (Phase-1-discovered bugfix batch).
**Originating diagnosis:** deep-fix-mode round 3 — suite-wide auth-tier CPU saturation traced to `LockoutUnlockService.ConfirmAsync` running O(N) Argon2id verifies over unconsumed-and-unexpired token rows. The pattern was already solved for `PasswordResetToken` and `EmailChangeToken` in Stage 6.15; `LockoutUnlockToken` was missed.
**Date:** 2026-05-17.
**Parent commit:** `2388935` (HEAD at spec-write).

---

## 1. The bug, in plain English

`LockoutUnlockService.ConfirmAsync` (`ProjectCeres/Common/Authentication/LockoutUnlockService.cs:124-138`) scans every unconsumed-and-unexpired `LockoutUnlockToken` row in the database, running Argon2id verify against each candidate's `TokenHash` until one matches the presented raw token:

```csharp
var candidates = await _db.LockoutUnlockTokens
    .IgnoreQueryFilters()
    .Where(t => t.ConsumedAt == null && t.ExpiresAt > now)
    .ToListAsync(ct);

foreach (var candidate in candidates)
{
    if (_tokens.Verify(rawToken, candidate.TokenHash))
    {
        match = candidate;
        break;
    }
}
```

Cost: O(N) per confirm, where N = unconsumed-and-unexpired row count. Argon2id at OWASP minimums (`m=19456 KiB, t=2, p=1` per `Argon2idOptions.cs:5-7`) runs ~50–100ms per verify on dev hardware.

**Production cost** is bounded: 1 token per user-lockout event over the 15-minute TTL. **Test cost** is unbounded: `LockoutUnlockConfirmTests` (17 tests) and `LockoutUnlockIssuanceTests` (~6 tests) each create unconsumed token rows in the shared `project_ceres_test` database within one ~4-minute suite run. Cumulative scan cost on the last `Confirm_*` test: 1–3 seconds of Argon2 work on a single xUnit worker thread.

**Downstream consequence**: CPU saturation on the same worker thread causes timing-sensitive tests on adjacent test surfaces (sliding-window rate-limit calculations, `Task.Delay`-based polling, concurrent confirm-ordering assertions) to miss their thresholds. The failure surface shifts run-to-run depending on xUnit scheduling. Spec §6.2 of the Stage 9.1.5.b spec already acknowledged this as the suite-wide flake the present spec resolves.

## 2. Why this fix is "extend Stage 6.15" not "design from scratch"

The exact pattern — HMAC-SHA256-keyed indexed lookup column — was designed, specced, shipped, and tested in Stage 6.15 (`docs/superpowers/specs/2026-05-11-stage-6-15-token-lookup-design.md`, migration `20260511155005_AddTokenLookup.cs`) for `PasswordResetToken` and `EmailChangeToken`. All supporting infrastructure already exists:

- `TokenLookupHasher` service (`Common/Authentication/TokenLookupHasher.cs`) — `byte[] ComputeLookup(string rawToken)` returns HMACSHA256(secret, UTF8(rawToken)), exactly 32 bytes.
- `TokenLookupOptions` (`Common/Authentication/TokenLookupOptions.cs`) — DI-bound secret keyed off `Authentication:TokenLookupSecret:Secret`.
- DbContext model config: `AppDbContext.cs:174-175, 187-188` — `b.HasIndex(e => e.TokenLookup).IsUnique(); b.Property(e => e.TokenLookup).HasMaxLength(32);`
- Tamper-resistance test pattern: `TokenLookupTamperResistanceTests.cs` covers PasswordReset + EmailChange.

`LockoutUnlockToken` was missed during Stage 6.15. The fix is to retrofit the existing pattern.

## 3. The fix

### 3.1 Model

`ProjectCeres/Models/LockoutUnlockToken.cs` — add one property between `UserId` and `TokenHash`:

```csharp
// HMAC-SHA256(serverSecret, rawToken). Unique index ensures /lockout-unlock
// locates the matching row in O(1) regardless of how many candidates exist
// (Stage 9.1.5.a — extends the Stage 6.15 pattern to the third token sibling).
public byte[] TokenLookup { get; set; } = Array.Empty<byte>();
```

Doc-comment matches the existing `EmailChangeToken.TokenLookup` comment word-for-word except for the route name and stage reference.

### 3.2 DbContext model configuration

`ProjectCeres/Data/AppDbContext.cs:195-204` (`ConfigureLockoutUnlockEntities`) — add two lines mirroring the PasswordReset/EmailChange configs:

```csharp
private static void ConfigureLockoutUnlockEntities(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<LockoutUnlockToken>(b =>
    {
        b.HasKey(e => e.Id);
        b.HasIndex(e => new { e.UserId, e.ConsumedAt });
        b.HasIndex(e => e.ExpiresAt);
        b.HasIndex(e => e.TokenLookup).IsUnique();                   // NEW
        b.Property(e => e.TokenLookup).HasMaxLength(32);             // NEW
        b.Property(e => e.TokenHash).HasMaxLength(512);
    });
}
```

### 3.3 EF migration

Generated via `dotnet ef migrations add AddLockoutUnlockTokenLookup`. The auto-generated `Up()` will need manual editing to insert the backfill-and-invalidate SQL between the `AddColumn` and the `CreateIndex` — exactly mirroring Stage 6.15's `20260511155005_AddTokenLookup.cs` pattern:

```csharp
public partial class AddLockoutUnlockTokenLookup : Migration
{
    /// <summary>
    /// Stage 9.1.5.a — extends the Stage 6.15 TokenLookup pattern to LockoutUnlockTokens
    /// so ConfirmAsync can locate a row in O(1) instead of running Argon2id against every
    /// unconsumed candidate. Closes the suite-wide CPU saturation that caused 9.1.5.a's
    /// shifting auth-tier test flakes.
    ///
    /// Existing rows cannot be HMACed (the raw token is gone) so the backfill writes
    /// a synthetic placeholder (md5(TokenHash || Id::text)) and stamps
    /// ConsumedAt = NOW() in the SAME statement — every pre-9.1.5.a token is
    /// invalidated by the migration. Acceptable because Phase 3 hosted-beta has one
    /// real user and no production lockout-unlock tokens worth preserving. Mirrors
    /// 20260511155005_AddTokenLookup.cs exactly.
    /// </summary>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "TokenLookup",
            table: "LockoutUnlockTokens",
            type: "bytea",
            maxLength: 32,
            nullable: false,
            defaultValue: new byte[0]);

        migrationBuilder.Sql(@"
            UPDATE ""LockoutUnlockTokens""
            SET ""ConsumedAt"" = NOW(),
                ""TokenLookup"" = decode(md5(""TokenHash"" || ""Id""::text), 'hex')
            WHERE ""ConsumedAt"" IS NULL;
        ");

        migrationBuilder.CreateIndex(
            name: "IX_LockoutUnlockTokens_TokenLookup",
            table: "LockoutUnlockTokens",
            column: "TokenLookup",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_LockoutUnlockTokens_TokenLookup",
            table: "LockoutUnlockTokens");

        migrationBuilder.DropColumn(
            name: "TokenLookup",
            table: "LockoutUnlockTokens");
    }
}
```

The MD5 placeholder is sufficient for the unique index — `TokenHash` already differs per row (per-row Argon2 salt), so `md5(TokenHash || Id::text)` is unique by construction. The output of `md5` is 16 bytes (32 hex chars); `decode(..., 'hex')` produces a 16-byte `bytea`. The column allows up to 32 bytes (matching real HMAC-SHA256 output for new rows); 16 vs 32 is just unused width.

### 3.4 Service — `LockoutUnlockService`

`ProjectCeres/Common/Authentication/LockoutUnlockService.cs`:

**Inject `TokenLookupHasher`** — add to constructor parameters after `IAuditLogWriter auditLog`:

```csharp
private readonly TokenLookupHasher _lookupHasher;

public LockoutUnlockService(
    // ... existing parameters ...
    IAuditLogWriter auditLog,
    LockoutCache lockoutCache,
    TokenLookupHasher lookupHasher)
{
    // ... existing assignments ...
    _lockoutCache = lockoutCache;
    _lookupHasher = lookupHasher;
}
```

(Stage 9.1.5.b already added `LockoutCache lockoutCache` to this ctor; this spec adds `TokenLookupHasher lookupHasher` after it.)

**`IssueAsync`** (line 82-90) — stamp `TokenLookup` when inserting the row:

```csharp
rawToken = _tokens.Generate();
var hash = _tokens.Hash(rawToken);
var lookup = _lookupHasher.ComputeLookup(rawToken);   // NEW
var now = DateTime.UtcNow;
_db.LockoutUnlockTokens.Add(new LockoutUnlockToken
{
    Id = Guid.NewGuid(),
    UserId = userId,
    TokenLookup = lookup,                              // NEW
    TokenHash = hash,
    CreatedAt = now,
    ExpiresAt = now + TokenLifetime,
    ConsumedAt = null,
});
```

**`ConfirmAsync`** (line 122-145) — replace the cross-tenant scan + `foreach` Argon2 loop with a single indexed lookup:

```csharp
var now = DateTime.UtcNow;
var lookup = _lookupHasher.ComputeLookup(rawToken);

// Cross-tenant by design: token-based pre-auth operation; caller is not in session.
// Stage 10 architecture test allow-lists this file. Stage 9.1.5.a: indexed lookup
// replaces the O(N) Argon2 scan; a single row matches the HMAC-derived TokenLookup
// or none does, so we run at most one Argon2 verify per request.
var candidate = await _db.LockoutUnlockTokens
    .IgnoreQueryFilters()
    .Where(t => t.TokenLookup == lookup && t.ConsumedAt == null && t.ExpiresAt > now)
    .SingleOrDefaultAsync(ct);

if (candidate is null || !_tokens.Verify(rawToken, candidate.TokenHash))
{
    // Constant-time: even with zero candidates, run one verify so timing doesn't
    // reveal "no rows" vs "rows but no Argon2 match". Mirrors the existing pattern
    // and preserves the anti-enumeration property of the original scan.
    if (candidate is null) _argon.RunDummyHash();
    return new LockoutUnlockOutcome.InvalidToken();
}

var match = candidate;
```

The downstream `_userLocks` semaphore wait, re-read inside lock, `ResetAccessFailedCountAsync`, `SetLockoutEndDateAsync(user, null)`, `LockoutCache.Remove(user.Email!)`, `ExecuteUpdateExactlyAsync`, and audit-log write are all **unchanged**. Only the candidate-finding code at lines 122-145 is replaced.

### 3.5 Files NOT touched

- `LockoutUnlockTokenGenerator.cs` — token generation/hash mechanism unchanged.
- `LockoutUnlockController.cs` — controller surface unchanged.
- `_userLocks` semaphore — orthogonal concern (multi-host migration is tracked under Stage 16).
- `LockoutCache` — Stage 9.1.5.b artifact, already shipped.
- Lockout duration, token TTL, audit-log action, unlock-email flow.

## 4. Tests

### 4.1 New tests

Two new tests, both in `ProjectCeres.Tests/Integration/Authentication/`.

**Test #1 — O(1) regression** (in `LockoutUnlockConfirmTests.cs`):

```csharp
[Fact]
public async Task Confirm_runs_at_most_one_Argon2_verify_regardless_of_unconsumed_token_count()
{
    // Seed 30 stale unconsumed-and-unexpired token rows for OTHER unrelated users.
    // Then issue + confirm a token for the test's own user.
    // Without TokenLookup: 30+ Argon2 verifies × ~80ms ≈ 2400ms+.
    // With TokenLookup: 1 verify, total elapsed < 500ms (HTTP overhead included).
    using (var scope = _factory.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<LockoutUnlockTokenGenerator>();
        var hasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
        for (int i = 0; i < 30; i++)
        {
            var noise = generator.Generate();
            db.LockoutUnlockTokens.Add(new LockoutUnlockToken
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                TokenLookup = hasher.ComputeLookup(noise),
                TokenHash = generator.Hash(noise),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow + LockoutUnlockService.TokenLifetime,
                ConsumedAt = null,
            });
        }
        await db.SaveChangesAsync();
    }

    var (_, rawToken) = await ArrangeLockedUserWithUnlockTokenAsync();

    var sw = Stopwatch.StartNew();
    var resp = await PostConfirmAsync(rawToken);
    sw.Stop();

    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    sw.ElapsedMilliseconds.Should().BeLessThan(500,
        "ConfirmAsync must run at most one Argon2 verify; the indexed TokenLookup lookup must return a single candidate row regardless of unconsumed-row count");
}
```

The 30-row seed is unrelated to the test's own user. The 30 noise rows use `Guid.NewGuid()` for `UserId` (won't collide with `DisposeAsync`'s `@lockout-confirm.local` cleanup filter, so they leak into the suite — see §4.3 for cleanup strategy). Each noise token has a real `TokenLookup` via the hasher, so the unique index doesn't trip.

**Test #2 — tamper resistance** (extends existing `TokenLookupTamperResistanceTests.cs`):

```csharp
[Fact]
public async Task LockoutUnlock_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401()
{
    // Mirror EmailChange_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401
    // (line 51 of this file). An attacker who guesses the HMAC-derived TokenLookup (lottery
    // odds) but presents the wrong raw token must still be rejected at the Argon2 verify step.
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
    var generator = scope.ServiceProvider.GetRequiredService<LockoutUnlockTokenGenerator>();

    var realToken = generator.Generate();
    var forgedToken = generator.Generate();  // different raw, same lookup forced below

    var user = await AuthTestFixture.RegisterUserAsync(_factory, $"tamper-{Guid.NewGuid():N}@tamper-test.local");

    db.LockoutUnlockTokens.Add(new LockoutUnlockToken
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        TokenLookup = lookupHasher.ComputeLookup(realToken),
        TokenHash = generator.Hash(forgedToken),    // tamper: hash doesn't match lookup
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow + LockoutUnlockService.TokenLifetime,
        ConsumedAt = null,
    });
    await db.SaveChangesAsync();

    var client = _factory.CreateClient();
    var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client,
        "/api/auth/lockout-unlock", new { token = realToken });

    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
        "matching TokenLookup but mismatched TokenHash must still fail Argon2 verify and return 401");
}
```

This test lives in `TokenLookupTamperResistanceTests.cs` next to its two siblings, not in `LockoutUnlockConfirmTests.cs`, to match the existing file's pattern.

### 4.2 Existing test updates (NOT NULL column constraint)

Four test-side `new LockoutUnlockToken { ... }` inserts will fail on `SaveChangesAsync` after the migration unless they stamp `TokenLookup`:

1. **`LockoutUnlockConfirmTests.cs:67`** — inside `ArrangeLockedUserWithUnlockTokenAsync` (shared setup helper used by 6+ tests).
2. **`LockoutUnlockConfirmTests.cs:146`** — `Confirm_with_expired_token_returns_401` setup.
3. **`LockoutUnlockConfirmTests.cs:417`** — (third setup site; unverified exact test name, but the grep flagged it).
4. **`LockoutUnlockIssuanceTests.cs:174`** — `Issue_supersedes_prior_unconsumed_token_for_same_user` setup (or sibling).

Each insert gets one new line: `TokenLookup = lookupHasher.ComputeLookup(rawToken),`. The hasher is resolved from the same DI scope already being used.

`ArrangeLockedUserWithUnlockTokenAsync` is the highest-leverage update — it's the canonical helper. Fixing it unblocks the 6+ tests that use it.

### 4.3 Test data cleanup for the noise rows in Test #1

The 30 noise rows use random `UserId` GUIDs and don't match any existing `DisposeAsync` cleanup filter. They'll accumulate in `project_ceres_test` across suite runs unless cleaned. Two acceptable mitigations:

- **(a)** Stamp the noise rows with `ExpiresAt = DateTime.UtcNow + TimeSpan.FromSeconds(2)`, then add `await Task.Delay(2500)` between seed and confirm. The expired rows fall out of `ConfirmAsync`'s `Where(t => t.ExpiresAt > now)` filter naturally, AND get garbage-collected by any future sweep. Total test wall-clock cost: +2.5s.
- **(b)** Inline cleanup: track the inserted IDs and delete them in a `finally` block at the end of the test. More code, no wall-clock cost.

**Recommend (b)** — explicit cleanup matches project preference for deterministic test state.

### 4.4 Existing test contract preservation

All 16 existing `Confirm_*` tests should pass unchanged. The contract change is internal to `ConfirmAsync`'s candidate-finding mechanism; observable HTTP behavior is identical (still consumes single-use, still returns 401 on bad token, still clears `AccessFailedCount`/`LockoutEnd`, still writes audit row, still invalidates `LockoutCache`).

The `Confirm_concurrent_two_callers_one_succeeds_one_returns_invalid_token` test (line 241) is the most interesting unchanged case: both callers compute the same HMAC lookup, both find the same single row, both enter the per-user semaphore. First call consumes; second sees `ConsumedAt != null` after the re-read, returns `InvalidToken`. Concurrency contract identical.

## 5. Edge cases

### 5.1 Migration-time row invalidation

The migration's `UPDATE … SET ConsumedAt = NOW(), TokenLookup = decode(md5(TokenHash || Id::text), 'hex')` invalidates any in-flight unconsumed token. Dev/test DB: typically zero in-flight rows at migration apply time. Production hosted-beta (one real user): zero unless the user happens to have requested an unlock link in the 15 minutes pre-deploy. If they did, their link breaks and they re-request. User consented to this trade-off (chose the Stage 6.15 backfill-and-invalidate pattern over a destructive DELETE).

### 5.2 `TokenLookup` uniqueness

HMAC-SHA256 output is 32 bytes (256 bits). Collision probability across the 15-minute TTL window is effectively zero (~1 in 2^128 for any pair). The unique index is correctness-load-bearing: if two rows ever share a `TokenLookup`, `SingleOrDefaultAsync` throws. That's the right failure mode (alarm on data corruption) rather than silent ambiguity.

The MD5-derived migration placeholder is 16 bytes; collision probability over the typical pre-migration row count (single digits) is also effectively zero. `TokenHash` differs per row by construction (per-row Argon2 salt), making `md5(TokenHash || Id::text)` deterministically unique even for the same raw token.

### 5.3 Constant-time anti-enumeration

The original scan ran `_argon.RunDummyHash()` when `candidates.Count == 0` (line 143). The new lookup runs `_argon.RunDummyHash()` when `candidate is null`. Same timing-channel mitigation, simpler code path. Mirrors the Stage 6.15 PasswordResetService and EmailChangeService implementations.

### 5.4 `TokenLookupHasher` secret rotation

Out of scope. The secret is shared across all three token tables; rotating it invalidates all in-flight tokens across all three services. Existing Stage 6.15 design accepts this constraint; `security-model.md § Secrets Rotation Procedures` documents it.

### 5.5 Concurrent issue-then-revoke

`IssueAsync` (line 64-96) is wrapped in a per-user semaphore that supersedes any prior unconsumed token for the same user before inserting the new one. The supersede `UPDATE ... SET ConsumedAt = NOW()` runs in the same transaction as the insert. Adding `TokenLookup` to the insert doesn't change the semaphore guarantee or the supersede ordering. The unique index on `TokenLookup` cannot trip during supersede because the superseded row already has a different (unique) lookup value from a different raw token.

## 6. Scope guard

### 6.1 In scope

1. New `LockoutUnlockToken.TokenLookup` property.
2. `AppDbContext.ConfigureLockoutUnlockEntities` — add 2 lines (unique index + max-length).
3. EF migration `AddLockoutUnlockTokenLookup`: AddColumn + backfill-and-invalidate SQL + CreateIndex (unique).
4. `LockoutUnlockService` constructor — inject `TokenLookupHasher`.
5. `LockoutUnlockService.IssueAsync` — stamp `TokenLookup` on row insert (~3 lines).
6. `LockoutUnlockService.ConfirmAsync` — replace candidate scan + Argon2 loop with indexed `SingleOrDefaultAsync` + single conditional verify (~10 lines net).
7. New `Confirm_runs_at_most_one_Argon2_verify_regardless_of_unconsumed_token_count` test.
8. New `LockoutUnlock_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401` test in `TokenLookupTamperResistanceTests.cs`.
9. Update 4 test-side `new LockoutUnlockToken { … }` inserts to stamp `TokenLookup`:
   - `LockoutUnlockConfirmTests.cs:67` (ArrangeLockedUserWithUnlockTokenAsync helper)
   - `LockoutUnlockConfirmTests.cs:146` (expired-token test setup)
   - `LockoutUnlockConfirmTests.cs:417` (third setup site)
   - `LockoutUnlockIssuanceTests.cs:174` (issuance test setup)
10. Roadmap close-out: flip 9.1.5.a `[ ]` to `[x]` in `docs/roadmap-phase-three.md`; replace "TBD pending architectural diagnosis" text with the post-fix description; remove the "diagnose suite-wide auth-tier test contention" framing.
11. Update task #43 description to reflect the shipped fix.

### 6.2 Out of scope (design choices, not deferrals)

- `TokenLookupHasher` service itself — already exists, no changes.
- `PasswordResetToken` / `EmailChangeToken` flow — already have the fix.
- `_userLocks` static dictionary — orthogonal multi-host concern (tracked under Stage 16).
- `LockoutCache` — Stage 9.1.5.b artifact, already shipped and invalidated in `ConfirmAsync` success path.
- `LockoutUnlockTokenGenerator` — token generation algorithm unchanged.
- Controller-layer changes (`LockoutUnlockController`) — request/response surface unchanged.
- Investigation of `Login_LimiterResetsAfterWindow` flake — if the CPU-saturation diagnosis is correct, that flake stops recurring as a side effect. If it doesn't, that's a separate root cause and deep-fix-mode re-fires.
- Secret rotation flow.

### 6.3 Deferred work (with required tripwire fields)

None. The fix is self-contained.

## 7. Verification checklist

- [ ] `LockoutUnlockToken.TokenLookup` property exists.
- [ ] `AppDbContext.ConfigureLockoutUnlockEntities` has the unique-index + max-length lines.
- [ ] EF migration `AddLockoutUnlockTokenLookup` created with the backfill-and-invalidate SQL between AddColumn and CreateIndex.
- [ ] `LockoutUnlockService.IssueAsync` stamps `TokenLookup`.
- [ ] `LockoutUnlockService.ConfirmAsync` uses indexed `SingleOrDefaultAsync` (no `foreach` candidate loop, no `ToListAsync` of unconsumed rows).
- [ ] All 4 test-side `new LockoutUnlockToken` inserts stamp `TokenLookup`.
- [ ] New `Confirm_runs_at_most_one_Argon2_verify_regardless_of_unconsumed_token_count` test green — total elapsed < 500ms.
- [ ] New `LockoutUnlock_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401` test green.
- [ ] Existing 16 `LockoutUnlockConfirmTests` green unchanged.
- [ ] Existing 6 `LockoutUnlockIssuanceTests` green unchanged.
- [ ] Migration applied cleanly to `project_ceres_test` dev DB (no constraint violations, no FK errors).
- [ ] Full `dotnet test` exits 0 across **three consecutive runs** — this is the diagnosis verification. If the suite-wide auth-tier flake stops recurring, the root cause is confirmed. If flakes persist on the same surfaces as before, deep-fix-mode re-fires.
- [ ] Roadmap entry `docs/roadmap-phase-three.md` 9.1.5.a checkbox flipped to `[x]`.
- [ ] Task #43 description updated to reflect resolution.

## 8. Open questions

None at spec-write time. All design decisions resolved during brainstorming + verify-against-codebase pre-flight:

- Backfill-and-invalidate pattern over DELETE — user reconsidered after verify-against-codebase surfaced the Stage 6.15 precedent; matches existing migration shape exactly.
- DbContext config location confirmed as inline `ConfigureLockoutUnlockEntities` method, not a separate `IEntityTypeConfiguration<>` file.
- `TokenLookupHasher.ComputeLookup` signature confirmed `byte[]` (32 bytes from HMACSHA256).
- 4 test-side insert sites enumerated and added to in-scope list.
- Three-consecutive-clean-run criterion preserved from the original 9.1.5.a checklist as the diagnosis-verification gate.
