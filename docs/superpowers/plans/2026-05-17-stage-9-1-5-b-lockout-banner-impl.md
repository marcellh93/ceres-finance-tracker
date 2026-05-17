# Stage 9.1.5.b — Lockout-banner-visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the locked-account banner visible to the user on attempt 11+ when the per-IP rate-limit short-circuits with `RATE_LIMITED`, without lowering `MaxFailedAccessAttempts` and without adding any DB read or body-buffering in `OnRejected`.

**Architecture:** A new `LockoutCache` singleton wraps `IMemoryCache` with two entry shapes — per-email `LockoutEnd` (TTL = `DefaultLockoutTimeSpan`) and per-IP "last locked email" pointer (TTL = 60s, carrying its own `lockedAt` for in-window honor). `AuthController.Login` seeds both entries on `signIn.IsLockedOut`. `Program.cs`'s `OnRejected` reads both (memory-only) for `/api/auth/login` requests and overrides the `RATE_LIMITED` envelope with `ACCOUNT_LOCKED_OUT` when both exist. `LockoutUnlockService.ConfirmAsync` invalidates the cache on successful unlock. `FailedLoginRecorder` consolidates email normalization onto `ILookupNormalizer` to share Identity's canonical normalizer across all three sites.

**Tech Stack:** .NET 10 · ASP.NET Core 10 · `Microsoft.Extensions.Caching.Memory` · `Microsoft.AspNetCore.RateLimiting` · xUnit · FluentAssertions · `WebApplicationFactory<Program>` · Npgsql + PostgreSQL.

**Source spec:** `docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md` (commit `c02220f`).

---

## Binding constraints

- **Stay on `main`.** No worktrees, no branches.
- **No `Co-Authored-By` trailer** in commit messages.
- **TDD per `docs/testing.md` § Rules** — write or update the failing test, run and confirm it fails for the right reason, then make it pass. If a test file is modified, the commit message states which of cases (1), (2), or (3) applied.
- **Never modify, skip, or weaken tests to make them pass.** No `[Fact(Skip=…)]`, no commented-out assertions, no `try/catch` to silence.
- **Pre-existing test failures encountered mid-task get root-caused now**, not logged as `TaskCreate` follow-ups. Per `feedback_never_skip_tests_to_make_them_pass`.
- **Stop-hook (`.claude/hooks/run-tests.sh`) blocks commits on `dotnet test` failure.** Each `git commit` step verifies the hook passed.
- **`pnpm` only** — no `npm` invocations anywhere (only relevant if a doc-touching subtask spawns CSS build, which Razor's pre-build MSBuild target does automatically; no JS work in this plan).
- **The `RateLimitTests` collection is subject to suite-wide auth-tier contention** that Stage 9.1.5.a tracks. Tests in this plan may flake under full-suite load; this is **NOT** a regression introduced by this fix. If a test from this plan passes in isolation (`dotnet test --filter "FullyQualifiedName~<TestName>"`) but fails in the full suite, document the symptom and proceed — do not block this stage on resolving 9.1.5.a.
- **Hook for new specs (`require-verify-against-codebase-before-spec.js`)** does not gate this plan because plans write under `docs/superpowers/plans/`, not `specs/`.

## File map

| File | Action | Purpose |
|---|---|---|
| `ProjectCeres/Common/Authentication/LockoutCacheOptions.cs` | **create** | Options bag with `EntryTtl` (default = `DefaultLockoutTimeSpan`, 15 min) and `IpPointerTtl` (default = `TimeSpan.FromSeconds(60)`). Lets the test factory shorten TTLs without reflection. |
| `ProjectCeres/Common/Authentication/LockoutCache.cs` | **create** | Singleton wrapper around `IMemoryCache`. Five methods: `TryGetLockoutEnd`, `SetLockoutEnd`, `TryGetLastLockedEmailForIp`, `SetLastLockedEmailForIp`, `Remove`. Takes `ILookupNormalizer` + `IOptions<LockoutCacheOptions>` via DI. |
| `ProjectCeres/Program.cs` | **modify** | (a) Register `LockoutCache` + `LockoutCacheOptions` in DI. (b) Extend `OnRejected` for `/api/auth/login` path to consult `LockoutCache` and surface `ACCOUNT_LOCKED_OUT` (401) instead of `RATE_LIMITED` (429) when both cache entries exist. |
| `ProjectCeres/Controllers/Api/AuthController.cs` | **modify** | Inject `LockoutCache`. On `signIn.IsLockedOut` branch (around line 190), seed both cache entries before returning the locked envelope. |
| `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` | **modify** | Inject `LockoutCache`. In `ConfirmAsync` success path (after `SetLockoutEndDateAsync(user, null)` at line 168), call `_lockoutCache.Remove(user.Email!)`. |
| `ProjectCeres/Common/Authentication/FailedLoginRecorder.cs` | **modify** | Replace local `TruncateAndNormalize` lowercase+trim with a call through `ILookupNormalizer.NormalizeEmail`. Truncation (256-char cap) stays. |
| `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs` | **modify** | Add `WithShortLockoutCacheTtl()` helper that post-configures `LockoutCacheOptions` with 1-second TTLs. |
| `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs` | **modify** | Add 4 new tests (`#1`–`#4` from spec §5.1); re-frame 3 existing tests (spec §5.3). |
| `ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs` | **modify** | One-line docstring update on `RateLimitRejection_DoesNotCountTowardLockout` per spec §5.4. |
| `docs/planning-phase3.md` | **modify** | Append one sentence to Stage 16 entry (line 449) per spec §3.2. |
| `docs/roadmap-phase-three.md` | **modify** | Replace 9.1.5.b verification text (line 1107) per spec §3.2. |

## Commit-by-commit overview

| Commit | Subject | What lands |
|---|---|---|
| 1 | `feat(stage-9.1.5.b): add LockoutCache + LockoutCacheOptions` | New files only, plus DI registration in `Program.cs`. No behavior change. |
| 2 | `feat(stage-9.1.5.b): seed LockoutCache on AuthController lockout transition` | `AuthController.cs` seeds both entries; new Test #1 turns green. |
| 3 | `feat(stage-9.1.5.b): surface ACCOUNT_LOCKED_OUT envelope from OnRejected when cache says locked` | `Program.cs` `OnRejected` extension; Test #1 still green; new Tests #2 and #3 land. |
| 4 | `feat(stage-9.1.5.b): per-IP pointer expiry — different user outside window returns RATE_LIMITED` | Test #4 + the test-factory helper `WithShortLockoutCacheTtl`. (Test #4 requires the same helper that #3 uses.) |
| 5 | `fix(stage-9.1.5.b): LockoutUnlockService invalidates LockoutCache on successful confirm` | One-line `LockoutCache.Remove` call after the DB unlock. |
| 6 | `refactor(stage-9.1.5.b): consolidate email normalization onto ILookupNormalizer` | `FailedLoginRecorder` uses `ILookupNormalizer`; existing tests pass unchanged. |
| 7 | `test(stage-9.1.5.b): re-frame existing rate-limit tests to use unregistered emails` | 3 existing tests in `RateLimitedAuthEndpointTests` switch to unregistered emails so they exercise the password-spray 429 path without colliding with the new lockout-aware OnRejected. |
| 8 | `docs(stage-9.1.5.b): close out Stage 9.1.5.b on the roadmap + Stage 16 amendment` | `roadmap-phase-three.md` line 1107 updated; `planning-phase3.md` line 449 amended; Stage 9.1.5.b verification checklist box ticked. |

Commits 1–4 are the load-bearing user-facing fix. Commit 5 closes the staleness gap. Commit 6 is the in-scope consolidation surfaced by the verify-against-codebase pre-flight. Commit 7 fixes collateral breakage from commits 1–4. Commit 8 closes the loop on docs.

---

### Task 1: Add LockoutCache + LockoutCacheOptions

**Files:**
- Create: `ProjectCeres/Common/Authentication/LockoutCacheOptions.cs`
- Create: `ProjectCeres/Common/Authentication/LockoutCache.cs`
- Modify: `ProjectCeres/Program.cs`

This commit introduces the cache type but does not yet seed it from `AuthController` or read it from `OnRejected`. The cache is dead code at the end of this commit; that's intentional — it isolates the "new abstraction lands" change from the "production paths start using it" change so each commit has one reason to fail.

- [ ] **Step 1: Verify `IMemoryCache` is already registered in DI**

Run: `grep -n "AddMemoryCache\|IMemoryCache" <repo>/ProjectCeres/Program.cs`
Expected: at least one line referencing `AddMemoryCache` or implicit registration via `AddDistributedMemoryCache` / `AddDataProtection`. If neither is present, this task adds `builder.Services.AddMemoryCache();` before the `LockoutCache` registration in Step 6.

- [ ] **Step 2: Write `LockoutCacheOptions.cs`**

```csharp
namespace ProjectCeres.Common.Authentication;

/// <summary>
/// TTL configuration for <see cref="LockoutCache"/>. Defaults match production
/// (15-minute lockout window for per-email entries, 60-second pointer expiry
/// for per-IP entries — aligned with the AuthLoginByIp sliding window). Tests
/// shorten both via PostConfigure to avoid wall-clock waits.
/// </summary>
public sealed class LockoutCacheOptions
{
    /// <summary>TTL for per-email "this account is locked until X" entries.
    /// Default = 15 minutes, matching Identity's <c>DefaultLockoutTimeSpan</c>.</summary>
    public TimeSpan EntryTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>TTL for per-IP "the last lockout from this IP was for email X at time T" pointer.
    /// Default = 60 seconds, matching the <c>AuthLoginByIp</c> rate-limit window so the
    /// pointer is only honored within one rate-limit cycle.</summary>
    public TimeSpan IpPointerTtl { get; set; } = TimeSpan.FromSeconds(60);
}
```

- [ ] **Step 3: Write `LockoutCache.cs`**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// In-memory hint cache used by <c>OnRejected</c> to surface ACCOUNT_LOCKED_OUT
/// instead of RATE_LIMITED when the per-IP rate-limit fires AFTER the account
/// has already been locked. Two entry shapes:
/// <list type="bullet">
///   <item><c>lockout:{normalizedEmail}</c> → <see cref="DateTimeOffset"/> of LockoutEnd. TTL = <see cref="LockoutCacheOptions.EntryTtl"/>.</item>
///   <item><c>last-login-email:{ip}</c> → (email, lockedAt). TTL = <see cref="LockoutCacheOptions.IpPointerTtl"/>. OnRejected only honors the pointer if lockedAt is within the IpPointerTtl window.</item>
/// </list>
/// Memory-only reads in OnRejected — no DB query — so the DoS-amplification
/// concern in security-model.md § lockout is preserved.
///
/// Single-host. Multi-host migration is tracked under Stage 16 alongside the
/// _loginLocks semaphore (see planning-phase3.md Stage 16 entry).
/// </summary>
public sealed class LockoutCache
{
    private const string EntryKeyPrefix = "lockout:";
    private const string PointerKeyPrefix = "last-login-email:";

    private readonly IMemoryCache _cache;
    private readonly ILookupNormalizer _normalizer;
    private readonly IOptions<LockoutCacheOptions> _options;

    public LockoutCache(IMemoryCache cache, ILookupNormalizer normalizer, IOptions<LockoutCacheOptions> options)
    {
        _cache = cache;
        _normalizer = normalizer;
        _options = options;
    }

    public bool TryGetLockoutEnd(string email, out DateTimeOffset lockoutEnd)
    {
        var key = EntryKey(email);
        if (key is null) { lockoutEnd = default; return false; }
        return _cache.TryGetValue(key, out lockoutEnd);
    }

    public void SetLockoutEnd(string email, DateTimeOffset until)
    {
        var key = EntryKey(email);
        if (key is null) return;
        _cache.Set(key, until, _options.Value.EntryTtl);
    }

    public bool TryGetLastLockedEmailForIp(string ip, out string normalizedEmail, out DateTimeOffset lockedAt)
    {
        normalizedEmail = string.Empty;
        lockedAt = default;
        if (string.IsNullOrWhiteSpace(ip)) return false;
        if (!_cache.TryGetValue<PointerEntry>(PointerKeyPrefix + ip, out var entry) || entry is null)
            return false;

        // Honor the pointer only if lockedAt is within IpPointerTtl. The IMemoryCache absolute
        // expiry already enforces this, but the explicit check defends against clock skew or
        // any future caller that reads the entry via a non-expiring path.
        if (DateTimeOffset.UtcNow - entry.LockedAt > _options.Value.IpPointerTtl) return false;
        normalizedEmail = entry.NormalizedEmail;
        lockedAt = entry.LockedAt;
        return true;
    }

    public void SetLastLockedEmailForIp(string ip, string email)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        var normalized = _normalizer.NormalizeEmail(email);
        if (string.IsNullOrEmpty(normalized)) return;
        _cache.Set(
            PointerKeyPrefix + ip,
            new PointerEntry(normalized, DateTimeOffset.UtcNow),
            _options.Value.IpPointerTtl);
    }

    public void Remove(string email)
    {
        var key = EntryKey(email);
        if (key is not null) _cache.Remove(key);
    }

    private string? EntryKey(string email)
    {
        var normalized = _normalizer.NormalizeEmail(email);
        return string.IsNullOrEmpty(normalized) ? null : EntryKeyPrefix + normalized;
    }

    private sealed record PointerEntry(string NormalizedEmail, DateTimeOffset LockedAt);
}
```

- [ ] **Step 4: Register `LockoutCache` + `LockoutCacheOptions` in `Program.cs`**

Modify `ProjectCeres/Program.cs`. After the existing `builder.Services.AddScoped<LockoutUnlockService>();` line (currently at line 147), add:

```csharp
builder.Services.AddOptions<LockoutCacheOptions>();
builder.Services.AddSingleton<LockoutCache>();
```

If Step 1 showed that `AddMemoryCache()` is not already registered, also add `builder.Services.AddMemoryCache();` before those two lines.

- [ ] **Step 5: Verify the project builds**

Run: `dotnet build <repo>/ProjectCeres/ProjectCeres.csproj`
Expected: `Build succeeded.` with 0 errors. No new warnings.

- [ ] **Step 6: Run the full server test suite to confirm zero regressions**

Run: `dotnet test <repo>/ProjectCeres.sln`
Expected: 0 failed. (If pre-existing flakes from Stage 9.1.5.a surface, re-run the specific test in isolation — `dotnet test --filter "FullyQualifiedName~<TestName>"` — to confirm it's the architectural contention 9.1.5.a tracks, not a regression from this commit. Document in the commit message if so. **Do not skip the test, do not log a TaskCreate follow-up — see binding constraints.**)

- [ ] **Step 7: Commit**

```bash
git -C <repo> add \
  ProjectCeres/Common/Authentication/LockoutCacheOptions.cs \
  ProjectCeres/Common/Authentication/LockoutCache.cs \
  ProjectCeres/Program.cs

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9.1.5.b): add LockoutCache + LockoutCacheOptions

New singleton wrapper around IMemoryCache with two entry shapes (per-email
LockoutEnd, per-IP last-locked-email pointer). Memory-only reads — designed
to be consulted by OnRejected without adding any DB query.

Wired into DI; no callers yet (LockoutCache is dead code at this commit).
Subsequent commits seed it from AuthController and read it from OnRejected.

Single-host; multi-host migration tracked under Stage 16 alongside the
existing _loginLocks semaphore.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-b-lockout-banner-impl.md
EOF
)"
```

Expected: commit succeeds, run-tests.sh hook is green.

---

### Task 2: Seed LockoutCache on AuthController lockout transition + Test #1

**Files:**
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`
- Test: `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs`

- [ ] **Step 1: Add the failing test `RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited`**

Open `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs`. Append the following test (above the closing `}` of the class):

```csharp
[Fact]
public async Task RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited()
{
    await using var factory = _factory.WithFreshRateLimiter();
    var user = await AuthTestFixture.RegisterUserAsync(factory, "rl-locked@rl-test.local");
    var client = factory.CreateClient();

    HttpResponseMessage? tenth = null;
    for (int i = 1; i <= 10; i++)
    {
        tenth = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
    }
    var tenthBody = await tenth!.Content.ReadFromJsonAsync<JsonElement>();
    tenthBody.GetProperty("error").GetProperty("code").GetString()
        .Should().Be("ACCOUNT_LOCKED_OUT",
            "attempt 10 trips MaxFailedAccessAttempts; controller returns ACCOUNT_LOCKED_OUT and seeds the lockout-cache");

    var eleventh = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
        new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
    eleventh.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
        "post-lockout 429s must be replaced by 401 ACCOUNT_LOCKED_OUT");
    var eleventhBody = await eleventh.Content.ReadFromJsonAsync<JsonElement>();
    eleventhBody.GetProperty("error").GetProperty("code").GetString()
        .Should().Be("ACCOUNT_LOCKED_OUT",
            "envelope must surface the lockout state so the SPA shows the locked-account banner");
}
```

- [ ] **Step 2: Run the new test, confirm it fails for the right reason**

Run: `dotnet test --filter "FullyQualifiedName~RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited"`
Expected: **FAIL** — assertion on attempt 11 expects `Unauthorized` (`ACCOUNT_LOCKED_OUT`) but gets `TooManyRequests` (`RATE_LIMITED`). The 10th-attempt assertion may also fail under the current code path depending on timing; that's still the "right reason" (the cache is not seeded yet AND/OR the 11th hit middleware before the controller-level lockout response sticks). If the 10th attempt asserts pass and only the 11th fails, that's the canonical failure mode of this commit.

- [ ] **Step 3: Modify `AuthController.cs` to inject `LockoutCache`**

Open `ProjectCeres/Controllers/Api/AuthController.cs`. Add the field next to the other private readonly fields (~line 39):

```csharp
    private readonly LockoutCache _lockoutCache;
```

Update the constructor (`public AuthController(...)`) to accept and store it. The current constructor signature ends with `Services.CategorySeedService categorySeedService` (line 53); add `LockoutCache lockoutCache` after it:

```csharp
    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        PersistentTokenService tokens,
        IAntiforgery antiforgery,
        FailedLoginRecorder failedLogins,
        IAuditLogWriter auditLog,
        LockoutUnlockService lockoutUnlock,
        ILogger<AuthController> logger,
        Services.CategorySeedService categorySeedService,
        LockoutCache lockoutCache)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _antiforgery = antiforgery;
        _failedLogins = failedLogins;
        _auditLog = auditLog;
        _lockoutUnlock = lockoutUnlock;
        _logger = logger;
        _categorySeedService = categorySeedService;
        _lockoutCache = lockoutCache;
    }
```

- [ ] **Step 4: Seed the cache on the `IsLockedOut` branch**

Locate the `if (signIn.IsLockedOut)` block (currently around line 190 in `AuthController.Login`). Inside the block, AFTER the existing `await _failedLogins.RecordAsync(...)` call but BEFORE the `if (lockoutTransitioned)` block, add:

```csharp
            // Seed LockoutCache so OnRejected can surface ACCOUNT_LOCKED_OUT on subsequent 429s.
            // Memory-only writes; no additional DB read. Per-IP pointer expires at IpPointerTtl
            // (60s = rate-limit window) so other accounts on the same NAT aren't mis-flagged
            // for longer than the rate-limit cooldown itself.
            if (userStub.LockoutEnd is { } lockoutEnd)
            {
                _lockoutCache.SetLockoutEnd(request.Email, lockoutEnd);
                _lockoutCache.SetLastLockedEmailForIp(ip, request.Email);
            }
```

(The `ip` variable is already in scope from the `(ip, ua) = RequestContext();` call earlier in the same branch.)

- [ ] **Step 5: Run the new test — confirm the controller seeds the cache but Test #1 still fails on attempt 11**

Run: `dotnet test --filter "FullyQualifiedName~RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited"`
Expected: **STILL FAIL** on attempt 11 (cache is seeded by attempt 10, but `OnRejected` doesn't read it yet — that's Task 3). The 10th-attempt assertion should pass.

This is the intended state for this commit: production code seeds the cache; the cache is read by nothing. Test #1 cannot pass until Task 3.

- [ ] **Step 6: Run a regression check on existing AuthController tests**

Run: `dotnet test --filter "FullyQualifiedName~LockoutBehaviorTests"`
Expected: all green. The constructor change is additive (new parameter); DI resolves it automatically because Task 1 registered `LockoutCache` as a singleton.

Also run: `dotnet test --filter "FullyQualifiedName~AuthController"`
Expected: all green.

- [ ] **Step 7: Commit (note: Test #1 still red — that's expected and explained in commit message)**

```bash
git -C <repo> add \
  ProjectCeres/Controllers/Api/AuthController.cs \
  ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9.1.5.b): seed LockoutCache on AuthController lockout transition

On the IsLockedOut branch in AuthController.Login, write the per-email
LockoutEnd and the per-IP last-locked-email pointer into LockoutCache.
Memory-only writes; no new DB read.

Test #1 (RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited)
lands as the load-bearing acceptance test. It is RED at this commit by design:
the cache is now seeded but OnRejected does not yet read it. Task 3 / Commit 3
turns Test #1 green.

This split-by-side keeps the "new behavior in the controller" and "new behavior
in middleware" changes in separate commits, each with one reason to fail.

Case applies (testing.md § Rules): case (3) — contract intentionally changed.
The contract now requires AuthController to seed LockoutCache on lockout
transitions, in addition to its existing IssueAsync + envelope return.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md §3.2
EOF
)"
```

Expected: commit succeeds. The stop-hook runs `dotnet test` — Test #1 fails. **If the stop-hook is configured to block on any failure, Tasks 2 + 3 must be merged into a single commit (see "Optional commit merge" note below).** Otherwise the commit lands red, with the explanation embedded in the message, and Task 3 turns it green.

**Optional commit merge:** If the stop-hook blocks Task 2's commit, abort the commit, complete Task 3 in the same working tree, then commit Tasks 2+3 together with a combined message:

```
feat(stage-9.1.5.b): seed LockoutCache + surface ACCOUNT_LOCKED_OUT from OnRejected

[Combined message of Task 2 and Task 3 commit bodies.]
```

The plan from Task 4 onward is identical either way.

---

### Task 3: OnRejected surfaces ACCOUNT_LOCKED_OUT envelope + Tests #2 and #3

**Files:**
- Modify: `ProjectCeres/Program.cs`
- Modify: `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs`
- Test: `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs`

- [ ] **Step 1: Add `WithShortLockoutCacheTtl` test-factory helper**

Open `ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs`. Add the following method after the existing `WithMediumLoginWindow` method (around line 120):

```csharp
    /// <summary>
    /// Returns a derived factory whose LockoutCache uses a 1-second TTL for BOTH the
    /// per-email lockout entries AND the per-IP last-login-email pointer (instead of the
    /// production 15-minute and 60-second values respectively). Use ONLY for tests that
    /// need both cache shapes to expire within test execution.
    /// </summary>
    public WebApplicationFactory<Program> WithShortLockoutCacheTtl() =>
        this.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<ProjectCeres.Common.Authentication.LockoutCacheOptions>(opts =>
                {
                    opts.EntryTtl = TimeSpan.FromSeconds(1);
                    opts.IpPointerTtl = TimeSpan.FromSeconds(1);
                });
            }));
```

- [ ] **Step 2: Add Test #2 — non-login endpoint still returns RATE_LIMITED**

Append to `RateLimitedAuthEndpointTests.cs`:

```csharp
[Fact]
public async Task RateLimitRejection_OnNonLoginEndpoint_StillReturnsRateLimited()
{
    var client = _factory.CreateClient();
    HttpResponseMessage? rejected = null;
    for (int i = 0; i < 70; i++)
    {
        var resp = await client.GetAsync("/api/auth/csrf");
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            rejected = resp;
            break;
        }
    }
    rejected.Should().NotBeNull("CSRF endpoint must still rate-limit at 60/min");

    var body = await rejected!.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("error").GetProperty("code").GetString()
        .Should().Be("RATE_LIMITED",
            "non-login endpoints must keep the original rate-limit envelope; the lockout-aware path is /api/auth/login only");
    rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
        "non-login endpoints must keep status 429");
}
```

- [ ] **Step 3: Add Test #3 — cache TTL expiry returns to RATE_LIMITED**

Append to `RateLimitedAuthEndpointTests.cs`:

```csharp
[Fact]
public async Task RateLimitRejection_AfterLockoutWindowExpires_FallsBackToRateLimitedEnvelope()
{
    await using var factory = _factory.WithShortLockoutCacheTtl();
    var user = await AuthTestFixture.RegisterUserAsync(factory, "rl-expire@rl-test.local");
    var client = factory.CreateClient();

    for (int i = 0; i < 10; i++)
    {
        await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
    }

    await Task.Delay(TimeSpan.FromMilliseconds(1200));

    HttpResponseMessage? rejected = null;
    for (int i = 0; i < 15; i++)
    {
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = "nosuchuser@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            rejected = resp;
            break;
        }
    }
    rejected.Should().NotBeNull("rate-limit must still fire after cache TTL expires");

    var body = await rejected!.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("error").GetProperty("code").GetString()
        .Should().Be("RATE_LIMITED",
            "after the lockout-cache TTL expires, OnRejected must fall back to the default RATE_LIMITED envelope");
}
```

- [ ] **Step 4: Run all three tests — confirm Test #1 still fails, Tests #2 and #3 fail for the right reasons**

Run: `dotnet test --filter "FullyQualifiedName~RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited|FullyQualifiedName~RateLimitRejection_OnNonLoginEndpoint_StillReturnsRateLimited|FullyQualifiedName~RateLimitRejection_AfterLockoutWindowExpires_FallsBackToRateLimitedEnvelope"`

Expected:
- **Test #1**: FAIL on attempt 11 (still — OnRejected not yet wired).
- **Test #2**: PASS — non-login endpoint already returns RATE_LIMITED (current behavior).
- **Test #3**: FAIL the same way Test #1 does — OnRejected isn't conditional yet.

The fact that Test #2 already passes is correct: it's a regression guard for the NEXT commit. We want to confirm that AFTER we change `OnRejected`, Test #2 still passes.

- [ ] **Step 5: Modify `Program.cs` `OnRejected` to consult `LockoutCache` for `/api/auth/login` requests**

Open `ProjectCeres/Program.cs`. Locate the `options.OnRejected = async (context, ct) => { ... }` block (currently lines 264–283). Replace its body with:

```csharp
    options.OnRejected = async (context, ct) =>
    {
        // SlidingWindowRateLimiter does not populate RetryAfter metadata on its denied
        // lease. Fall back to the segment duration (Window / SegmentsPerWindow = 15 s)
        // so the header is always present, as required by the API contract.
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)retryAfter.TotalSeconds
            : 15;
        context.HttpContext.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        // Stage 9.1.5.b: when rejecting /api/auth/login, consult LockoutCache. If the
        // requesting IP's last-attempted email is known to be locked, surface the
        // ACCOUNT_LOCKED_OUT envelope (401) instead of RATE_LIMITED (429). Memory-only
        // reads — no DB query — so the DoS-amplification concern in security-model.md
        // § lockout is preserved.
        if (context.HttpContext.Request.Path.StartsWithSegments("/api/auth/login"))
        {
            var lockoutCache = context.HttpContext.RequestServices
                .GetRequiredService<ProjectCeres.Common.Authentication.LockoutCache>();
            var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            if (lockoutCache.TryGetLastLockedEmailForIp(ip, out var lockedEmail, out _)
                && lockoutCache.TryGetLockoutEnd(lockedEmail, out _))
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = new
                    {
                        code = "ACCOUNT_LOCKED_OUT",
                        message = "Account temporarily locked. Try again in 15 minutes.",
                    }
                }, ct);
                return;
            }
        }

        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                code = "RATE_LIMITED",
                message = "Too many requests. Please retry shortly.",
            }
        }, ct);
    };
```

Note: `OnRejected` must explicitly set `StatusCode = 401` on the locked branch because `options.RejectionStatusCode = StatusCodes.Status429TooManyRequests` (line 262) only takes effect when the handler doesn't override it.

- [ ] **Step 6: Run the three target tests — Tests #1, #2, #3 should all pass**

Run: `dotnet test --filter "FullyQualifiedName~RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited|FullyQualifiedName~RateLimitRejection_OnNonLoginEndpoint_StillReturnsRateLimited|FullyQualifiedName~RateLimitRejection_AfterLockoutWindowExpires_FallsBackToRateLimitedEnvelope"`

Expected: **all 3 PASS.**

If Test #3 fails at the "FireUntilRateLimited" stage (the new burst from a different unregistered email never trips 429 because the rate-limit budget has rolled over), increase the inner loop count from 15 to 25 or use `factory.WithFreshRateLimiter()` for the burst phase — but only after confirming the failure is rate-limit-window related and not OnRejected logic.

- [ ] **Step 7: Run the broader rate-limit suite to check for collateral breakage**

Run: `dotnet test --filter "FullyQualifiedName~RateLimitedAuthEndpointTests"`

Expected: all green EXCEPT possibly the 3 existing tests Task 7 will re-frame (`Login_RequestsEventuallyReturn429WithRetryAfter`, `RateLimit429_ResponseBodyMatchesApiContractEnvelope`, `RateLimitOnRejected_ContentTypeIsApplicationJson`). Those tests register a user and drive 429 with wrong passwords; under the new behavior, attempt 10 may trigger lockout and `OnRejected` returns `ACCOUNT_LOCKED_OUT` (status 401), not `RATE_LIMITED` (status 429). The `FireUntilRateLimited` helper looks for status 429 — if attempt 10 returns 401 the helper doesn't recognize it and keeps firing until the loop exhausts, returning null, and the test fails with "expected rate limit to fire within 25 attempts".

This is the expected collateral breakage that Task 7 fixes. **Do not weaken or skip those tests now** — Task 7 re-frames them to use unregistered emails (which never trigger lockout) so they still test the per-IP rate-limit shape correctly.

- [ ] **Step 8: Commit**

```bash
git -C <repo> add \
  ProjectCeres/Program.cs \
  ProjectCeres.Tests/Integration/RateLimitedAuthTestWebApplicationFactory.cs \
  ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9.1.5.b): surface ACCOUNT_LOCKED_OUT envelope from OnRejected when cache says locked

Extends OnRejected to consult LockoutCache for /api/auth/login rejections. If
both the per-IP pointer (within IpPointerTtl) and the per-email entry exist,
return 401 ACCOUNT_LOCKED_OUT instead of 429 RATE_LIMITED. Memory-only reads;
no DB query on the rejection path.

Test #1 (RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited)
now passes — the load-bearing acceptance test for this stage.

Test #2 (RateLimitRejection_OnNonLoginEndpoint_StillReturnsRateLimited) passes —
regression guard: /api/auth/csrf and every other rate-limited endpoint still
emits RATE_LIMITED.

Test #3 (RateLimitRejection_AfterLockoutWindowExpires_FallsBackToRateLimitedEnvelope)
passes — the cache override is bounded in time; after EntryTtl/IpPointerTtl expire,
RATE_LIMITED resumes.

Adds WithShortLockoutCacheTtl helper on RateLimitedAuthTestWebApplicationFactory
so Tests #3 and #4 don't need 15-minute wall-clock waits.

Known collateral: 3 existing tests in RateLimitedAuthEndpointTests that register
a user and drive 429 with wrong passwords will now see 401 ACCOUNT_LOCKED_OUT on
attempt 10+; the FireUntilRateLimited helper looks for 429 and will time out.
Task 7 / Commit 7 re-frames those 3 tests to use unregistered emails so they
still exercise the per-IP rate-limit shape correctly.

Case applies (testing.md § Rules): case (3) — contract intentionally changed.
The /api/auth/login rejection contract now returns 401 ACCOUNT_LOCKED_OUT when
the requesting IP's last-attempted email is known-locked; existing per-IP
rate-limit behavior preserved for all other endpoints.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md §3.1, §3.2
EOF
)"
```

Expected: commit succeeds, stop-hook is green (Test #1 is now passing, Tests #2 and #3 are passing, the 3 existing tests are not yet broken at this commit because the hook re-runs ALL tests — wait, they ARE broken at this commit. See note.)

**IMPORTANT NOTE on stop-hook:** the 3 existing tests `Login_RequestsEventuallyReturn429WithRetryAfter`, `RateLimit429_ResponseBodyMatchesApiContractEnvelope`, and `RateLimitOnRejected_ContentTypeIsApplicationJson` may turn red at this commit because they register a user before firing wrong passwords. If the stop-hook blocks this commit due to those failures, choose:

(a) **Merge Tasks 3 + 7 into a single commit.** Combined message: `feat(stage-9.1.5.b): surface ACCOUNT_LOCKED_OUT from OnRejected; re-frame collateral tests`. Order of operations: complete Task 3 Steps 1–7, then immediately do Task 7's test re-framing without committing in between, then run the full suite and commit both together.

(b) **Run the 3 affected tests first to confirm they actually break** before deciding. They may not — `FireUntilRateLimited` runs up to 25 iterations, and if the 10th attempt's 401 doesn't stop the loop, attempts 11–25 hit the rate-limit ceiling and the helper finds a 429 in the broader RATE_LIMITED-fallback path because... wait, no. Attempt 11+ from a registered locked user returns 401 (`OnRejected` finds the cache), not 429. The helper would never find a 429. The tests will fail.

**Recommendation: pre-merge Tasks 3 + 7 in this case.** The split-by-side preference (separate commits per logical unit) yields to the hard constraint (stop-hook blocks red commits).

---

### Task 4: Test #4 — per-IP pointer expiry

**Files:**
- Test: `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs`

This test asserts the per-IP pointer's expiry behavior: a different user from the same IP, after the 60s window elapses, gets `RATE_LIMITED` (not `ACCOUNT_LOCKED_OUT` from the prior user's pointer).

- [ ] **Step 1: Add Test #4**

Append to `RateLimitedAuthEndpointTests.cs`:

```csharp
[Fact]
public async Task RateLimitRejection_DifferentUserFromSameIp_OutsideWindow_ReturnsRateLimited()
{
    await using var factory = _factory.WithShortLockoutCacheTtl();
    var userA = await AuthTestFixture.RegisterUserAsync(factory, "rl-ip-a@rl-test.local");
    var userB = await AuthTestFixture.RegisterUserAsync(factory, "rl-ip-b@rl-test.local");
    var client = factory.CreateClient();

    for (int i = 0; i < 10; i++)
    {
        await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = userA.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
    }

    await Task.Delay(TimeSpan.FromMilliseconds(1200));

    var bResp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
        new { email = userB.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
    bResp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
        "outside the per-IP pointer window, a different user's 429 must keep its RATE_LIMITED shape");
    var body = await bResp.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("error").GetProperty("code").GetString()
        .Should().Be("RATE_LIMITED",
            "user B is not locked; the per-IP pointer to user A must have expired");
}
```

- [ ] **Step 2: Run Test #4**

Run: `dotnet test --filter "FullyQualifiedName~RateLimitRejection_DifferentUserFromSameIp_OutsideWindow_ReturnsRateLimited"`

Expected: PASS — the cache TTL helper from Task 3 expires both the per-email entry and the per-IP pointer within 1s. User B's request arrives 1.2s after user A's lockout, so the pointer is gone; `OnRejected` falls through to `RATE_LIMITED`.

If FAIL with "expected TooManyRequests but got Unauthorized (401)": the per-IP pointer TTL is being treated as longer than the test expects, OR the explicit `if (DateTimeOffset.UtcNow - entry.LockedAt > _options.Value.IpPointerTtl) return false;` check in `TryGetLastLockedEmailForIp` is not firing. Re-read Task 1 Step 3's `LockoutCache` definition and confirm the time check uses `>` (not `>=`).

If FAIL with "expected TooManyRequests but got something else (e.g. 401 INVALID_CREDENTIALS)": user B's 10 attempts of wrong password from user A drained the rate-limit budget; user B's first attempt may go through the controller and return INVALID_CREDENTIALS. Check the rate-limit budget — if it's not exhausted, the test needs additional setup attempts to saturate it. (The current setup fires exactly 10 attempts for user A, matching `PermitLimit = 10`. The 11th attempt — user B's — should trip the 429.)

- [ ] **Step 3: Run the broader rate-limit suite to confirm no new collateral**

Run: `dotnet test --filter "FullyQualifiedName~RateLimitedAuthEndpointTests"`

Expected: all green EXCEPT the same 3 existing tests Task 7 fixes (if Task 3 didn't merge with Task 7 above).

- [ ] **Step 4: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9.1.5.b): per-IP pointer expiry — different user outside window returns RATE_LIMITED

Test #4 (RateLimitRejection_DifferentUserFromSameIp_OutsideWindow_ReturnsRateLimited)
pins the per-IP pointer's 60s honor window. Two users sharing an IP (corporate NAT,
public WiFi); user A locks out; 1.2s after the pointer TTL has elapsed, user B's
429 must surface as RATE_LIMITED, not ACCOUNT_LOCKED_OUT.

This is the residual-risk bound from spec §4.1: within the IpPointerTtl window
user B may see the wrong banner once (acceptable transient false positive
documented in the spec); outside the window the envelope must be correct.

No production-code change in this commit — the behavior was already correct
because LockoutCache.TryGetLastLockedEmailForIp explicitly checks IpPointerTtl
in addition to the IMemoryCache absolute expiry.

Case applies (testing.md § Rules): case (3) — contract intentionally changed.
This test asserts the IpPointerTtl-based expiry contract introduced in commits
1–3 of Stage 9.1.5.b.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md §4.1, §5.1
EOF
)"
```

Expected: commit succeeds.

---

### Task 5: LockoutUnlockService invalidates cache on successful confirm

**Files:**
- Modify: `ProjectCeres/Common/Authentication/LockoutUnlockService.cs`

This closes the staleness gap from spec §4.3 — user manually unlocks via the email link before the cache entry's natural TTL elapses; without invalidation the cache would still say "locked" for up to 15 minutes after the DB is cleared.

No new test required for this commit's core mechanism — the spec's §5.4 explicitly notes existing `LockoutUnlockConfirmTests` assert DB state (not cache state), and the cache invalidation is observable only through the absence of a downstream bug. Cache state is exercised by Tests #1–#4 indirectly.

However, **one new test** pins the contract: after a successful confirm, the cache returns false for the previously locked email.

- [ ] **Step 1: Add the failing test**

Open `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs` and append (above the closing `}`):

```csharp
[Fact]
public async Task Confirm_RemovesEmailFromLockoutCache()
{
    // Setup: register a user, force them into the lockout state, seed the LockoutCache
    // as the production AuthController.Login flow would.
    var user = await AuthTestFixture.RegisterUserAsync(_factory, "cache-invalidate@unlock-test.local");
    using (var scope = _factory.Services.CreateScope())
    {
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByIdAsync(user.Id.ToString());
        await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        var cache = scope.ServiceProvider.GetRequiredService<ProjectCeres.Common.Authentication.LockoutCache>();
        cache.SetLockoutEnd(user.Email!, DateTimeOffset.UtcNow.AddMinutes(15));
        cache.TryGetLockoutEnd(user.Email!, out _).Should().BeTrue(
            "test precondition: cache entry must exist before Confirm runs");
    }

    // Issue a token via the service (matches production flow) and consume it.
    string rawToken;
    using (var scope = _factory.Services.CreateScope())
    {
        var unlock = scope.ServiceProvider.GetRequiredService<LockoutUnlockService>();
        await unlock.IssueAsync(user.Id, user.Email!, "127.0.0.1", "test-agent", "http://localhost", CancellationToken.None);
        // IssueAsync writes a token row but does not return it. Read the most recent unconsumed
        // raw token via the test fixture helper or by reflecting on the in-memory email sink.
        rawToken = await AuthTestFixture.ReadLatestLockoutUnlockTokenFromEmailAsync(_factory, user.Email!);
    }

    LockoutUnlockOutcome outcome;
    using (var scope = _factory.Services.CreateScope())
    {
        var unlock = scope.ServiceProvider.GetRequiredService<LockoutUnlockService>();
        outcome = await unlock.ConfirmAsync(rawToken, CancellationToken.None);
    }
    outcome.Should().BeOfType<LockoutUnlockOutcome.Success>();

    // The cache entry must be gone — invalidation happens inside ConfirmAsync's success path.
    using (var scope = _factory.Services.CreateScope())
    {
        var cache = scope.ServiceProvider.GetRequiredService<ProjectCeres.Common.Authentication.LockoutCache>();
        cache.TryGetLockoutEnd(user.Email!, out _).Should().BeFalse(
            "ConfirmAsync success path must call _lockoutCache.Remove(email) so a subsequent burst doesn't see a stale 'locked' hint");
    }
}
```

**Note on `AuthTestFixture.ReadLatestLockoutUnlockTokenFromEmailAsync`**: if this helper does not exist, the existing `LockoutUnlockConfirmTests` should have an equivalent — read one of its tests to find the canonical token-retrieval pattern and adapt. If no fixture helper exists, retrieve the raw token directly from the in-memory email sink fixture used by other unlock-confirm tests.

- [ ] **Step 2: Run the test, confirm it fails**

Run: `dotnet test --filter "FullyQualifiedName~Confirm_RemovesEmailFromLockoutCache"`
Expected: **FAIL** at the final assertion — `TryGetLockoutEnd` returns `true` because `ConfirmAsync` doesn't yet remove the cache entry.

- [ ] **Step 3: Modify `LockoutUnlockService.cs` to invalidate the cache**

Inject `LockoutCache` into the constructor. Add the field next to the other private readonly fields (~line 38):

```csharp
    private readonly LockoutCache _lockoutCache;
```

Append the constructor parameter and assignment (after `IAuditLogWriter auditLog` at line 50):

```csharp
    public LockoutUnlockService(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        LockoutUnlockTokenGenerator tokens,
        IEmailService email,
        IEmailComposer composer,
        IEmailRecipientResolver recipients,
        ILanguageResolver languages,
        ILogger<LockoutUnlockService> logger,
        IAuditLogWriter auditLog,
        LockoutCache lockoutCache)
    {
        _userManager = userManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _email = email;
        _composer = composer;
        _recipients = recipients;
        _languages = languages;
        _logger = logger;
        _auditLog = auditLog;
        _lockoutCache = lockoutCache;
    }
```

In `ConfirmAsync`, after `await _userManager.SetLockoutEndDateAsync(user, null);` (line 168) and BEFORE the `ExecuteUpdateExactlyAsync` block, add:

```csharp
            // Stage 9.1.5.b: invalidate the LockoutCache hint so OnRejected doesn't surface
            // a stale "locked" envelope on the user's next request burst. The DB row is now
            // unlocked; the cache must follow.
            if (!string.IsNullOrEmpty(user.Email)) _lockoutCache.Remove(user.Email);
```

- [ ] **Step 4: Run the new test — expect PASS**

Run: `dotnet test --filter "FullyQualifiedName~Confirm_RemovesEmailFromLockoutCache"`
Expected: PASS.

- [ ] **Step 5: Run the broader unlock-confirm suite to confirm no regressions**

Run: `dotnet test --filter "FullyQualifiedName~LockoutUnlockConfirmTests"`
Expected: all green. (Stage 9.1.5.a notes that this test class is one of the surfaces that flakes under full-suite load. If a test passes in isolation but fails in the broader filter run, this is the documented architectural contention — see binding constraints.)

- [ ] **Step 6: Commit**

```bash
git -C <repo> add \
  ProjectCeres/Common/Authentication/LockoutUnlockService.cs \
  ProjectCeres.Tests/Integration/Authentication/LockoutUnlockConfirmTests.cs

git -C <repo> commit -m "$(cat <<'EOF'
fix(stage-9.1.5.b): LockoutUnlockService invalidates LockoutCache on successful confirm

After ConfirmAsync clears AccessFailedCount + LockoutEnd in the DB, call
LockoutCache.Remove(email) so OnRejected does not surface a stale
ACCOUNT_LOCKED_OUT envelope for the user's next request burst.

Closes the staleness gap from spec §4.3: without this, the cache entry
persists for up to EntryTtl (15 min by default) after the DB unlock,
making the user's next burst incorrectly see "locked" until the natural
TTL expires.

New test Confirm_RemovesEmailFromLockoutCache pins the contract.

Case applies (testing.md § Rules): case (3) — contract intentionally changed.
ConfirmAsync's success path now invalidates LockoutCache in addition to its
existing DB unlock + token consume + audit write.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md §3.2, §4.3
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

### Task 6: Consolidate email normalization onto ILookupNormalizer

**Files:**
- Modify: `ProjectCeres/Common/Authentication/FailedLoginRecorder.cs`

Closes spec §4.6's normalization-drift concern. `FailedLoginRecorder.TruncateAndNormalize` currently does its own lowercase+trim; `LockoutCache` uses `ILookupNormalizer`; `UserManager.NormalizeEmail` uses it too. Consolidating onto `ILookupNormalizer` ensures all three sites produce identical keys.

- [ ] **Step 1: Confirm no test pins the current local-normalization behavior**

Run: `grep -rn "TruncateAndNormalize\|FailedLoginRecorder.*Normalize" <repo>/ProjectCeres.Tests --include="*.cs"`
Expected: zero hits, or only hits that assert downstream observable behavior (e.g. "the recorded `EmailAttempted` is lowercase"). If the latter, those tests will continue to pass because `ILookupNormalizer.NormalizeEmail` returns uppercase by default — see Step 3's caveat.

**Caveat on Identity's default normalizer:** `ILookupNormalizer.NormalizeEmail` returns the email in **UPPERCASE** by default (Identity's `UpperInvariantLookupNormalizer`). This differs from the current `TruncateAndNormalize` which returns lowercase. If any existing test asserts the recorded `EmailAttempted` is lowercase, this consolidation introduces a contract change for those tests — they must be updated as part of this commit, OR `LockoutCache` and `FailedLoginRecorder` must use a custom `ILookupNormalizer` registered in DI that preserves lowercase output.

**Decision tree:**
- If the grep returns zero hits AND no existing test asserts `EmailAttempted.Should().Be("lowercase@…")`: proceed with `ILookupNormalizer.NormalizeEmail` (uppercase output). The consolidation is clean.
- If any test asserts lowercase output: change the strategy — define a custom `ILookupNormalizer` (e.g. `LowercaseLookupNormalizer`) in `ProjectCeres/Common/Authentication/`, register it in DI replacing the default, and proceed. This is a larger change but keeps `EmailAttempted` lowercase.

- [ ] **Step 2: Grep for any test asserting `EmailAttempted` casing**

Run: `grep -rn "EmailAttempted.*Should\|EmailAttempted.*Be(" <repo>/ProjectCeres.Tests --include="*.cs"`
Expected: zero hits OR hits that assert specific email values. Inspect any hits to see if they expect lowercase.

**If zero hits or all hits expect uppercase**: proceed with the standard `ILookupNormalizer` (uppercase output).

**If any hit expects lowercase**: STOP this task. Re-design Step 3 to use a custom `LowercaseLookupNormalizer` and re-register it in DI. Then rewrite `LockoutCache` to use the same custom normalizer (no `LockoutCache` code change needed — DI resolves the new normalizer automatically). Update plan accordingly.

For this plan, the assumption is **standard `ILookupNormalizer` (uppercase output)** unless Step 2 surfaces lowercase-asserting tests.

- [ ] **Step 3: Modify `FailedLoginRecorder.cs`**

Open `ProjectCeres/Common/Authentication/FailedLoginRecorder.cs`. Add `ILookupNormalizer` field, inject in constructor:

```csharp
using Microsoft.AspNetCore.Identity;
// (existing usings retained)

public class FailedLoginRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILookupNormalizer _normalizer;

    public FailedLoginRecorder(IServiceScopeFactory scopeFactory, ILookupNormalizer normalizer)
    {
        _scopeFactory = scopeFactory;
        _normalizer = normalizer;
    }

    // ... existing RecordAsync method unchanged ...

    private string? TruncateAndNormalize(string? input, int max)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var normalized = _normalizer.NormalizeEmail(input.Trim()) ?? input.Trim();
        return normalized.Length > max ? normalized[..max] : normalized;
    }

    private static string Truncate(string input, int max)
        => input.Length > max ? input[..max] : input;
}
```

The `TruncateAndNormalize` method is no longer `static` (it needs the instance `_normalizer`). The static `Truncate` method (used for `UserAgent`) is unchanged.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test`
Expected: all green. If any test fails on `EmailAttempted` casing, Step 2 missed a hit — go back and apply the "custom LowercaseLookupNormalizer" path.

- [ ] **Step 5: Commit**

```bash
git -C <repo> add \
  ProjectCeres/Common/Authentication/FailedLoginRecorder.cs

git -C <repo> commit -m "$(cat <<'EOF'
refactor(stage-9.1.5.b): consolidate email normalization onto ILookupNormalizer

FailedLoginRecorder.TruncateAndNormalize previously did its own lowercase+trim.
LockoutCache (introduced in commit 1 of this stage) uses ILookupNormalizer.
UserManager.NormalizeEmail uses ILookupNormalizer. Consolidating
FailedLoginRecorder onto the same normalizer ensures all three sites produce
identical keys, closing the drift concern from spec §4.6.

Truncation logic (256-char cap) stays. The method is no longer static because
it now depends on the injected ILookupNormalizer instance.

Case applies (testing.md § Rules): no test files modified. Production-only
refactor; existing tests pass unchanged.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md §4.6
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

### Task 7: Re-frame existing rate-limit tests to use unregistered emails

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs`

(Skip this task if it was merged into Task 3 per the stop-hook note above.)

Three tests register a user and drive 429 with wrong passwords. Under the new behavior, attempt 10 triggers lockout and attempt 11+ from the SAME registered user returns 401 `ACCOUNT_LOCKED_OUT` instead of 429 `RATE_LIMITED`. The tests want to assert per-IP rate-limit shape, not lockout shape — so switch the email to an unregistered address (no DB row, no lockout possible).

- [ ] **Step 1: Inspect each of the 3 affected tests**

Open `ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs`. Confirm the current shape of each:

1. `Login_RequestsEventuallyReturn429WithRetryAfter` (around line 47)
2. `RateLimit429_ResponseBodyMatchesApiContractEnvelope` (around line 62)
3. `RateLimitOnRejected_ContentTypeIsApplicationJson` (around line 117)

Each currently calls `AuthTestFixture.RegisterUserAsync(_factory, "<some>@rl-test.local")` and then drives wrong passwords for that registered email.

- [ ] **Step 2: Re-frame Test #1 — `Login_RequestsEventuallyReturn429WithRetryAfter`**

Replace the test body with:

```csharp
[Fact]
public async Task Login_RequestsEventuallyReturn429WithRetryAfter()
{
    // Use an UNREGISTERED email so we exercise the per-IP rate-limit path without
    // triggering account lockout (which would cause OnRejected to surface
    // ACCOUNT_LOCKED_OUT 401 instead of RATE_LIMITED 429 from Stage 9.1.5.b onward).
    var client = _factory.CreateClient();

    var rejected = await FireUntilRateLimited(() =>
        AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "nouser-rl@rl-test.local", password = "x-long-enough-x", rememberMe = false }));

    rejected.Should().NotBeNull("expected rate limit to fire within 25 attempts");
    rejected!.Headers.RetryAfter.Should().NotBeNull();
}
```

The only changes are: (a) drop the `RegisterUserAsync` call, (b) change the email from `"rl@rl-test.local"` to `"nouser-rl@rl-test.local"`.

- [ ] **Step 3: Re-frame Test #2 — `RateLimit429_ResponseBodyMatchesApiContractEnvelope`**

```csharp
[Fact]
public async Task RateLimit429_ResponseBodyMatchesApiContractEnvelope()
{
    // Unregistered email — see Login_RequestsEventuallyReturn429WithRetryAfter rationale.
    var client = _factory.CreateClient();

    var rejected = await FireUntilRateLimited(() =>
        AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "nouser-envelope@rl-test.local", password = "x-long-enough-x", rememberMe = false }));

    rejected.Should().NotBeNull("expected rate limit to fire within 25 attempts");
    var body = await rejected!.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("RATE_LIMITED");
    body.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrEmpty();
}
```

- [ ] **Step 4: Re-frame Test #3 — `RateLimitOnRejected_ContentTypeIsApplicationJson`**

```csharp
[Fact]
public async Task RateLimitOnRejected_ContentTypeIsApplicationJson()
{
    // Unregistered email — see Login_RequestsEventuallyReturn429WithRetryAfter rationale.
    var client = _factory.CreateClient();

    var rejected = await FireUntilRateLimited(() =>
        AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "nouser-contenttype@rl-test.local", password = "x-long-enough-x", rememberMe = false }));

    rejected.Should().NotBeNull("expected rate limit to fire within 25 attempts");
    rejected!.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
}
```

- [ ] **Step 5: Run the full rate-limit endpoint test class**

Run: `dotnet test --filter "FullyQualifiedName~RateLimitedAuthEndpointTests"`
Expected: all green.

- [ ] **Step 6: Run the full server test suite to confirm no other collateral**

Run: `dotnet test`
Expected: all green (modulo Stage 9.1.5.a flakes — see binding constraints).

- [ ] **Step 7: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Tests/Integration/Authentication/RateLimitedAuthEndpointTests.cs

git -C <repo> commit -m "$(cat <<'EOF'
test(stage-9.1.5.b): re-frame existing rate-limit tests to use unregistered emails

Three tests previously registered a user and drove 429 with wrong passwords:
  - Login_RequestsEventuallyReturn429WithRetryAfter
  - RateLimit429_ResponseBodyMatchesApiContractEnvelope
  - RateLimitOnRejected_ContentTypeIsApplicationJson

Under the new contract (commit 3 of this stage), attempt 10 against a
registered email triggers account lockout and attempt 11+ from the SAME email
returns 401 ACCOUNT_LOCKED_OUT instead of 429 RATE_LIMITED. The
FireUntilRateLimited helper looks for 429 and would time out.

The tests' intent is to assert the per-IP rate-limit shape (status, envelope,
RetryAfter, content-type) — NOT the lockout shape. Switching to unregistered
emails keeps the intent intact: no DB row exists for the attempted email, so
no lockout is possible; only the per-IP rate-limit fires; OnRejected returns
the RATE_LIMITED envelope as before.

Case applies (testing.md § Rules): case (3) — contract intentionally changed.
The /api/auth/login contract introduced in commits 1–3 of this stage causes
registered-user 429 paths to surface ACCOUNT_LOCKED_OUT; these tests are
re-framed to exercise the still-valid unregistered-user 429 path. Test
intent (per-IP rate-limit shape) is preserved.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md §5.3
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

### Task 8: Roadmap + Stage-16 docs close-out

**Files:**
- Modify: `docs/planning-phase3.md` (line 449 area)
- Modify: `docs/roadmap-phase-three.md` (line 1107)
- Modify: `ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs` (one-line docstring update from spec §5.4)

- [ ] **Step 1: Amend `planning-phase3.md:449` Stage 16 entry**

Open `docs/planning-phase3.md`. Locate the Stage 16 entry starting at line 449 (`8. **Login lockout: replace per-user semaphore with atomic single-statement UPDATE** ...`). Append the following sentence to the end of that entry's paragraph (right before the closing period or after the existing "See commit 6a5133b." reference, whichever lands cleanly in prose):

```
Lockout-status cache (`LockoutCache`, currently `IMemoryCache`, single-host) also needs multi-host migration alongside `_loginLocks` — shares the same single-host assumption (Stage 9.1.5.b commit 1).
```

- [ ] **Step 2: Replace `roadmap-phase-three.md:1107` verification text**

Open `docs/roadmap-phase-three.md`. Locate line 1107 (current text: `- [ ] 9.1.5.b — typing 6 wrong passwords in a row produces \`401 ACCOUNT_LOCKED_OUT\` deterministically (not \`429\`); xUnit regression test pins the threshold + the ordering between rate-limit and lockout-counter increment`).

Replace with:

```
- [x] 9.1.5.b — at attempt 11+ on `/api/auth/login` (after lockout has engaged at attempt 10), the response is `401 ACCOUNT_LOCKED_OUT`, NOT `429 RATE_LIMITED`. `MaxFailedAccessAttempts` stays at 10 (security-model.md §lockout invariant). `LockoutCache` hint mechanism: `AuthController.Login` seeds per-email + per-IP-pointer cache entries on lockout; `OnRejected` consults them (memory-only, no DB query) for `/api/auth/login` rejections and surfaces the locked envelope. Per-IP pointer expires at 60s; cache invalidated by `LockoutUnlockService.ConfirmAsync`. Tests `RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited`, `RateLimitRejection_OnNonLoginEndpoint_StillReturnsRateLimited`, `RateLimitRejection_AfterLockoutWindowExpires_FallsBackToRateLimitedEnvelope`, `RateLimitRejection_DifferentUserFromSameIp_OutsideWindow_ReturnsRateLimited`, `Confirm_RemovesEmailFromLockoutCache` pin the contract. Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-b-lockout-banner-impl.md`. Manual browser verification: enter 10 wrong passwords; attempt 11+ shows the locked-account banner (i18n key `auth.errors.account_locked_out`), NOT the rate-limit toast.
```

The leading `[x]` flips from `[ ]` to `[x]` because this stage is now done.

- [ ] **Step 3: One-line docstring update on `LockoutBehaviorTests.RateLimitRejection_DoesNotCountTowardLockout`**

Open `ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs`. Locate the `RateLimitRejection_DoesNotCountTowardLockout` test (line 131). Update the existing comment block (lines 134–138) to add a Stage-9.1.5.b note:

```csharp
    public async Task RateLimitRejection_DoesNotCountTowardLockout()
    {
        // The default test factory has the no-op limiter so we exercise this
        // logically. The recorder/controller wiring must NOT call AccessFailedAsync
        // on the rate-limit-rejection path. Architecture test #51 enforces this
        // structurally; here we sanity-check at the integration level by firing
        // many requests (no-op limiter accepts them all) and confirming each one
        // contributes exactly 1 to AccessFailedCount.
        //
        // Stage 9.1.5.b note: this test uses the no-op rate-limit factory, so
        // OnRejected never runs and the LockoutCache lookup path is not exercised
        // here. The cache-aware envelope behavior is covered by
        // RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited
        // and siblings in RateLimitedAuthEndpointTests.
```

- [ ] **Step 4: Run the full test suite to ensure docstring-only test edit broke nothing**

Run: `dotnet test --filter "FullyQualifiedName~LockoutBehaviorTests"`
Expected: all green.

- [ ] **Step 5: Verify the doc updates by reading them back**

Run: `grep -n "Stage 9.1.5.b\|LockoutCache" <repo>/docs/planning-phase3.md <repo>/docs/roadmap-phase-three.md`
Expected: the amended Stage 16 line and the new roadmap line 1107 both visible.

- [ ] **Step 6: Commit**

```bash
git -C <repo> add \
  docs/planning-phase3.md \
  docs/roadmap-phase-three.md \
  ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs

git -C <repo> commit -m "$(cat <<'EOF'
docs(stage-9.1.5.b): close out on roadmap + Stage 16 amendment

- docs/roadmap-phase-three.md:1107 — replace 9.1.5.b verification text with the
  shipped contract (lockout banner at attempt 11+, not "ACCOUNT_LOCKED_OUT at
  6 attempts"); flip the checkbox to [x].
- docs/planning-phase3.md:449 — append one sentence to the Stage 16 entry noting
  LockoutCache also needs multi-host migration alongside _loginLocks
  (already-scheduled deferral per no-unjustified-deferrals procedure;
  receiving stage entry is Stage 16 itself).
- ProjectCeres.Tests/Integration/Authentication/LockoutBehaviorTests.cs —
  one-line docstring update on RateLimitRejection_DoesNotCountTowardLockout
  noting it uses the no-op rate-limit factory (per spec §5.4).

Case applies (testing.md § Rules): test file modified for docstring-only edit;
no assertions changed. Production behavior unaffected.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md §3.2, §6.3
EOF
)"
```

Expected: commit succeeds, stop-hook is green. Stage 9.1.5.b is now closed out.

---

## Manual browser verification (post-commit-8)

After all 8 commits land:

- [ ] **Start the dev server.** `cd <repo> && dotnet run --project ProjectCeres`
- [ ] **Open the SPA login page** at `http://localhost:5081/app/login` (or the project's local URL).
- [ ] **Burst 10 wrong passwords** against the seeded dev user account. Expected on each: `INVALID_CREDENTIALS` toast for attempts 1–9; `ACCOUNT_LOCKED_OUT` banner on attempt 10 (the controller's response).
- [ ] **Burst 1 more wrong password (attempt 11).** Expected: `ACCOUNT_LOCKED_OUT` banner (not a rate-limit toast). The unlock email is in the dev maildir (`tmp/maildir/` or whatever the local IEmailService writes to).
- [ ] **Wait the rate-limit window out (~60s)** and retry one more wrong password. Expected: `ACCOUNT_LOCKED_OUT` banner (controller sees the persisted `LockoutEnd`).
- [ ] **Click the unlock email link.** Expected: account unlocks; SPA navigates to `/app/login` with an "unlocked" state.
- [ ] **Log in with correct password.** Expected: success.

If any step deviates, stop and root-cause — do not log as a follow-up.

---

## Self-review

**Spec coverage:**
- §1 the bug — covered by Task 2 + 3 (tests pin the symptom; production code fixes it).
- §2 why not lower threshold — preserved as binding constraint; no commit changes `MaxFailedAccessAttempts`.
- §3.1 mechanism (three cache entries) — Task 1 (cache type), Task 2 (seed), Task 3 (read), Task 5 (invalidate). All four operations covered.
- §3.2 files touched — every file in spec §3.2 mapped to a task.
- §3.3 rejected alternatives — preserved in spec, not re-litigated in plan.
- §4 edge cases — §4.1 covered by Test #4 (Task 4). §4.2 covered by Test #3 (Task 3). §4.3 covered by Task 5. §4.4 covered by Task 1's cache design (in-memory, repopulates on first miss). §4.5 covered by Stage 16 amendment (Task 8). §4.6 covered by Task 6. §4.7 covered by current `[ApiController]` validation behavior (no code change needed).
- §5.1 tests — Tests #1–#4 all in plan as Tasks 2, 3 (×2), 4.
- §5.2 test infra — `WithShortLockoutCacheTtl` in Task 3.
- §5.3 re-framed tests — Task 7.
- §5.4 unchanged tests + docstring — Task 8 covers the docstring; the other unchanged tests are explicitly preserved by not touching them.
- §6.1 in scope — every item mapped to a task.
- §6.2 out of scope — no task addresses these; intentional.
- §6.3 deferrals — multi-host amended in Stage 16 entry by Task 8.
- §7 verification — every checklist item maps to a Task acceptance criterion or to the manual verification block.

**Placeholder scan:** searched the plan for "TODO", "TBD", "implement later", "as appropriate", "etc.". Zero hits. Every step contains the exact content the engineer needs.

**Type consistency:** `LockoutCache`'s method names are consistent across Tasks 1, 2, 3, 4, 5. The cache-key prefixes (`lockout:`, `last-login-email:`) appear only in Task 1's private constants — never duplicated elsewhere. Constructor parameter ordering is consistent in each modified type.

**Hook safety:** the stop-hook risk is called out explicitly at Task 2 Step 7 and Task 3 Step 8 with a concrete merge-tasks fallback. The plan does not assume the hook will tolerate red commits.

**TDD ordering:** every task that introduces production behavior writes the failing test first, confirms it fails for the right reason, then implements the production change. Task 1 (no behavior change) and Task 8 (doc-only) are the only exceptions; both call this out in their task descriptions.
