# Stage 9.11 — Playwright E2E Foundations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Five browser-driven golden-path auth suites (register→login, password-reset, TOTP-enrol→first-login, lockout→unlock, backup-code-recovery) running against the real app (Kestrel + production SPA bundle + real PostgreSQL + RLS) on Chromium/Firefox/WebKit, runnable with `pnpm e2e`.

**Architecture:** A new `ASPNETCORE_ENVIRONMENT=E2E` boots the production bundle (no Vite dev middleware) against a dedicated `project_ceres_e2e` database. A file-sink `IEmailService` writes emails as JSON so tests can read verify/reset/unlock links (tokens are Argon2id-hashed in the DB — the link is the only way in). A wrapper script (`tools/e2e/run-server.sh`) bootstraps + migrates + wipes the DB, builds + stages the SPA bundle, asserts the manifest, then boots the app over HTTPS (mandatory — `__Host-` cookies need Secure). Playwright's `webServer` runs that script. Production-code touches are env-gated and pinned by architecture tests so the E2E email-sink / HIBP stub / raised rate limits can never resolve outside E2E.

**Tech Stack:** Playwright (`@playwright/test` — already a devDep), `otpauth` (new devDep, TOTP code generation), .NET 10 / ASP.NET Core, EF Core + Npgsql, PostgreSQL with RLS roles (`ceres_app`/`ceres_admin`/`ceres_migrator`).

**Spec:** `docs/superpowers/specs/2026-06-11-stage-9-11-playwright-e2e-foundations-design.md` (commit c5c2eb0). **Branch:** `stage-9.11-playwright-e2e-foundations` (already created; spec commit `c5c2eb0` is HEAD).

---

## Key facts the worker must not rediscover

- **The `e2e/` directory already exists** (shipped 2026-05-26): `ProjectCeres.Client/e2e/playwright.config.ts`, `agent-walk.spec.ts`, `pages/{LoginPage,RegisterPage,DashboardPage,AuthLayoutPage}.ts`, `flows/auth.ts`, `.gitignore`. `@playwright/test ^1.60.0` and `tsx` are already devDeps. The `e2e` pnpm script exists (`playwright test --config e2e/playwright.config.ts`). **Extend, don't recreate.** The agent-walk harness must keep working.
- **An `HibpStubBreachedPasswordChecker` already exists** in the test project (`ProjectCeres.Tests/Integration/WafCollection.cs:92` registers it). The production-side E2E stub is new code (separate assembly), but mirror its always-not-breached behavior.
- **`/api/health` exists** (polled by `tools/agent-env/up.sh:167`). The E2E webServer reuses it as the readiness probe.
- **The `Smoke` launch profile + `--urls` override** is the precedent for a custom environment booting on a chosen port (`up.sh:147-150`). E2E follows the same shape but over HTTPS.
- **InputOTP** renders shadcn slots with stable `[data-slot="input-otp-slot"]` selectors and a hidden `[data-slot="input-otp"] input`. Typing 6 digits **auto-submits** on `/login/totp` and the TOTP enroll step. Fill via the hidden input.
- **No `data-testid` attributes** exist on the auth pages except `data-testid="totp-qr-wrapper"`. Suites select by role/label/text (EN strings are in the selector map within each suite task).
- **Cookie names** (`ProjectCeres/Common/Authentication/SessionConstants.cs`): `__Host-Session`, `__Host-Persist`, `__Host-XSRF`; CSRF header `X-XSRF-TOKEN`. The CSRF request token comes from the `GET /api/auth/csrf` **response header**, never the cookie value, and rotates on login/logout.

## File map

**Production code (`ProjectCeres/`):**
- Modify: `Common/Authentication/EmailConfirmationService.cs:134` (URL `/app` prefix)
- Modify: `Common/Authentication/LockoutUnlockService.cs:131` (URL `/app` prefix)
- Create: `Common/Email/FileSinkEmailService.cs`
- Create: `Common/Authentication/AlwaysAllowBreachedPasswordChecker.cs` (E2E HIBP stub)
- Create: `Common/RateLimiting/RateLimitOptions.cs` (config-bound limits)
- Create: `Common/E2eDatabaseGuardStartupCheck.cs` (boot guard)
- Modify: `Program.cs` (FileSink + HIBP-stub E2E branches, rate-limit binding, boot guard)
- Create: `appsettings.E2E.json` (tracked, secret-free)

**Tests (`ProjectCeres.Tests/`):**
- Modify: `Integration/AppRole/EmailConfirmationUnderRlsTests.cs:69` (assertion → `/app/email-verify#token=`)
- Modify: `Integration/Authentication/LockoutUnlockIssuanceTests.cs:93` (assertion → `/app/account/unlock#token=`)
- Modify: `Integration/Authentication/ArchitectureTests.cs` (knownImpls + E2E-resolution pins)
- Create: `Integration/Configuration/E2eEnvironmentRegistrationTests.cs` (FileSink/HIBP/rate-limit pins)
- Create: `Unit/Email/FileSinkEmailServiceTests.cs` (JSON shape)

**E2E (`ProjectCeres.Client/e2e/`):**
- Modify: `playwright.config.ts` (narrow testMatch to agent-walk)
- Create: `playwright.golden.config.ts`
- Create: `tsconfig.e2e.json`; modify `tsconfig.json` (add reference)
- Modify: `package.json` (scripts + `otpauth` devDep)
- Create: `support/{api,emails,totp,users}.ts`
- Create: `auth/{register-login,password-reset,totp-enrol-and-first-login,lockout-self-service,backup-code-recovery}.spec.ts`
- Modify: `.gitignore` (`.artifacts/`, email sink)
- Modify: `vite.config.ts:33` (stale proxy port) + `vite.config.ts:49-53` (rollup inputs)

**Tooling / docs:**
- Create: `tools/e2e/run-server.sh`
- Modify: `docs/testing.md`, `docs/decisions/ADR-0071-e2e-testing-on-playwright.md`, `docs/roadmap-phase-three.md`, `docs/api-contract.md`

---

## Phase 1 — Production-code foundations (backend, no browser)

### Task 1: Fix the two broken emailed-URL prefixes (tests-first)

**Files:**
- Modify: `ProjectCeres.Tests/Integration/AppRole/EmailConfirmationUnderRlsTests.cs:69`
- Modify: `ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs:93`
- Modify: `ProjectCeres/Common/Authentication/EmailConfirmationService.cs:134`
- Modify: `ProjectCeres/Common/Authentication/LockoutUnlockService.cs:131`

- [ ] **Step 1: Update both pinning assertions to the `/app`-prefixed shape (red first).**

In `EmailConfirmationUnderRlsTests.cs`, line 69 currently reads:
```csharp
        var confirmEmail = captured.Should().ContainSingle(m => m.BodyText.Contains("email-verify#token="),
            "register issues exactly one confirmation email").Which;
```
Change the substring to the prefixed form, and update the comment on line 67-68 to match:
```csharp
        // Step 2: extract the raw token from the confirmation email. Register emits exactly one
        // message and its URL is of the form {base}/app/email-verify#token={raw}; select by that
        // URL and reuse the public ExtractResetTokenFromMessage helper (generic "token=" marker).
        var confirmEmail = captured.Should().ContainSingle(m => m.BodyText.Contains("/app/email-verify#token="),
            "register issues exactly one confirmation email").Which;
```

In `LockoutUnlockIssuanceTests.cs`, line 93 currently reads:
```csharp
        unlockEmails[0].BodyText.Should().Contain("/account/unlock#token=");
```
Change it to:
```csharp
        unlockEmails[0].BodyText.Should().Contain("/app/account/unlock#token=");
```

- [ ] **Step 2: Run both tests, verify they FAIL.**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~LockoutUnlockIssuanceTests.Login_transitioning_into_lockout_writes_token_row_AND_queues_email|FullyQualifiedName~EmailConfirmationUnderRlsTests"`
Expected: FAIL — both assert the `/app/`-prefixed substring which the service does not yet emit. (Because `"email-verify#token="` is a substring of `"/app/email-verify#token="`, only the *new* expectation goes red — the production string still lacks the prefix.)

- [ ] **Step 3: Add the `/app` prefix in `EmailConfirmationService.cs`.**

Line 134 currently reads:
```csharp
        var verifyUrl = $"{verifyUrlBase.TrimEnd('/')}/email-verify#token={rawToken}";
```
Change to (matching the correct `PasswordResetService.cs:188` precedent):
```csharp
        var verifyUrl = $"{verifyUrlBase.TrimEnd('/')}/app/email-verify#token={rawToken}";
```

- [ ] **Step 4: Add the `/app` prefix in `LockoutUnlockService.cs`.**

Line 131 currently reads:
```csharp
        var unlockUrl = $"{unlockUrlBase.TrimEnd('/')}/account/unlock#token={rawToken}";
```
Change to:
```csharp
        var unlockUrl = $"{unlockUrlBase.TrimEnd('/')}/app/account/unlock#token={rawToken}";
```

- [ ] **Step 5: Update the stale XML-doc URL shape in `EmailConfirmationTests.cs`.**

Open `ProjectCeres.Tests/Integration/EmailConfirmationTests.cs` around line 61. If a comment or XML doc names the old `{base}/email-verify#token=` shape, update it to `{base}/app/email-verify#token=`. (This file's runtime assertion uses a generic `token=` marker — only the comment is stale. If no such comment exists at :61 after the other edits, skip.)

- [ ] **Step 6: Run both tests, verify they PASS.**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~LockoutUnlockIssuanceTests.Login_transitioning_into_lockout_writes_token_row_AND_queues_email|FullyQualifiedName~EmailConfirmationUnderRlsTests"`
Expected: PASS.

- [ ] **Step 7: Run the broader auth suite to catch any other assertion on the old shape.**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~Authentication"`
Expected: PASS. If any other test asserts `"/email-verify#token="` or `"/account/unlock#token="` without `/app`, update it the same way (the SPA pages live at `/app/...` per `App.tsx`; the old root paths 404 in a browser — the bug being fixed).

- [ ] **Step 8: Commit.**

```bash
git add ProjectCeres/Common/Authentication/EmailConfirmationService.cs ProjectCeres/Common/Authentication/LockoutUnlockService.cs ProjectCeres.Tests/Integration/AppRole/EmailConfirmationUnderRlsTests.cs ProjectCeres.Tests/Integration/Authentication/LockoutUnlockIssuanceTests.cs ProjectCeres.Tests/Integration/EmailConfirmationTests.cs
git commit -m "fix(9.11): /app prefix on verify + unlock emailed URLs — root paths 404 in browser

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `FileSinkEmailService` + unit test

**Files:**
- Create: `ProjectCeres/Common/Email/FileSinkEmailService.cs`
- Create: `ProjectCeres.Tests/Unit/Email/FileSinkEmailServiceTests.cs`

- [ ] **Step 1: Write the failing unit test.**

Create `ProjectCeres.Tests/Unit/Email/FileSinkEmailServiceTests.cs`:
```csharp
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectCeres.Common.Email;
using Xunit;

namespace ProjectCeres.Tests.Unit.Email;

public class FileSinkEmailServiceTests
{
    private static EmailMessage SampleMessage() => new(
        To: EmailRecipient.ForTest("user@example.test"),
        Subject: "Confirm your email",
        BodyHtml: "<a href=\"https://localhost/app/email-verify#token=abc\">verify</a>",
        BodyText: "Verify: https://localhost/app/email-verify#token=abc");

    [Fact]
    public async Task SendAsync_writes_one_json_file_with_the_expected_shape()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ceres-emailsink-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var sut = new FileSinkEmailService(dir, NullLogger<FileSinkEmailService>.Instance);

            await sut.SendAsync(SampleMessage(), CancellationToken.None);

            var files = Directory.GetFiles(dir, "*.json");
            files.Should().ContainSingle();

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(files[0]));
            var root = doc.RootElement;
            root.GetProperty("to").GetString().Should().Be("user@example.test");
            root.GetProperty("subject").GetString().Should().Be("Confirm your email");
            root.GetProperty("bodyText").GetString().Should().Contain("/app/email-verify#token=abc");
            root.GetProperty("bodyHtml").GetString().Should().Contain("/app/email-verify#token=abc");
            root.TryGetProperty("sentAtUtc", out _).Should().BeTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
```

- [ ] **Step 2: Check whether `EmailRecipient.ForTest` exists; if not, use the real factory.**

Run: `grep -n "public static EmailRecipient" ProjectCeres/Common/Email/EmailRecipient.cs`
The factories may be `internal`. If there is no public/test-accessible factory, replace `EmailRecipient.ForTest("user@example.test")` in the test with whatever the project's existing tests use to build an `EmailRecipient` (search: `grep -rn "EmailRecipient\." ProjectCeres.Tests` and copy the pattern — e.g. an existing test helper). The test must compile against the real type; do not add a new public factory just for this.

- [ ] **Step 3: Run the test, verify it FAILS (type not defined).**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~FileSinkEmailServiceTests"`
Expected: FAIL — `FileSinkEmailService` does not exist.

- [ ] **Step 4: Implement `FileSinkEmailService`.**

Create `ProjectCeres/Common/Email/FileSinkEmailService.cs` (model the SendAsync signature + terse style on `LogOnlyEmailService.cs`; keep the XML doc ≤3 sentences per project rule):
```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProjectCeres.Common.Email;

/// <summary>
/// E2E-only implementation: writes each message as one JSON file into a sink
/// directory so Playwright can read verify/reset/unlock links (tokens are
/// Argon2id-hashed in the DB, so the email is the only way to get the raw link).
/// Registered ONLY under ASPNETCORE_ENVIRONMENT=E2E; never in Production/Development.
/// </summary>
public sealed class FileSinkEmailService : IEmailService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly ILogger<FileSinkEmailService> _logger;

    public FileSinkEmailService(string directory, ILogger<FileSinkEmailService> logger)
    {
        _directory = directory;
        _logger = logger;
        Directory.CreateDirectory(_directory);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var payload = new
        {
            to = message.To.Address,
            subject = message.Subject,
            bodyText = message.BodyText,
            bodyHtml = message.BodyHtml,
            sentAtUtc = DateTimeOffset.UtcNow,
        };
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(_directory, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions), ct);
        _logger.LogInformation("[email/e2e-sink] wrote {Path} To={To}", path, message.To.Address);
    }
}
```

- [ ] **Step 5: Run the test, verify it PASSES.**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~FileSinkEmailServiceTests"`
Expected: PASS.

- [ ] **Step 6: Commit.**

```bash
git add ProjectCeres/Common/Email/FileSinkEmailService.cs ProjectCeres.Tests/Unit/Email/FileSinkEmailServiceTests.cs
git commit -m "feat(9.11): FileSinkEmailService — JSON email sink for E2E (env-gated registration in Task 6)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: E2E HIBP stub (production assembly)

**Files:**
- Create: `ProjectCeres/Common/Authentication/AlwaysAllowBreachedPasswordChecker.cs`

- [ ] **Step 1: Read the interface to copy the exact signature.**

Run: `cat ProjectCeres/Common/Authentication/IBreachedPasswordChecker.cs`
Expected: a single method `Task<bool> IsBreachedAsync(string password, CancellationToken ct = default)`.

- [ ] **Step 2: Implement the stub.**

Create `ProjectCeres/Common/Authentication/AlwaysAllowBreachedPasswordChecker.cs`:
```csharp
namespace ProjectCeres.Common.Authentication;

/// <summary>
/// E2E-only checker that treats every password as not-breached, so E2E registration
/// never makes a live HIBP API call (an outage would otherwise 500 the register
/// endpoint and flake the suite). Registered ONLY under ASPNETCORE_ENVIRONMENT=E2E;
/// the real <see cref="HaveIBeenPwnedPasswordChecker"/> keeps its production coverage.
/// </summary>
public sealed class AlwaysAllowBreachedPasswordChecker : IBreachedPasswordChecker
{
    public Task<bool> IsBreachedAsync(string password, CancellationToken ct = default) =>
        Task.FromResult(false);
}
```

- [ ] **Step 3: Verify it compiles.**

Run: `dotnet build ProjectCeres`
Expected: Build succeeded, 0 errors. (The type is unused until Task 6 wires it; that's fine.)

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres/Common/Authentication/AlwaysAllowBreachedPasswordChecker.cs
git commit -m "feat(9.11): AlwaysAllowBreachedPasswordChecker — E2E HIBP stub (env-gated in Task 6)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Config-bound rate limits

**Files:**
- Create: `ProjectCeres/Common/RateLimiting/RateLimitOptions.cs`
- Modify: `ProjectCeres/Program.cs` (bind options; replace literals in the three relevant limiters)
- Create: `ProjectCeres.Tests/Integration/Configuration/RateLimitOptionsDefaultsTests.cs`

- [ ] **Step 1: Write the failing defaults test.**

Create `ProjectCeres.Tests/Integration/Configuration/RateLimitOptionsDefaultsTests.cs`:
```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ProjectCeres.Common.RateLimiting;
using Xunit;

namespace ProjectCeres.Tests.Integration.Configuration;

public class RateLimitOptionsDefaultsTests
{
    // With NO configuration overrides, the bound values must equal the literals that
    // shipped before Stage 9.11 — so the config binding can never silently weaken
    // production. Raised values live ONLY in appsettings.E2E.json.
    [Fact]
    public void Unconfigured_options_equal_the_pre_9_11_production_literals()
    {
        var config = new ConfigurationBuilder().Build(); // empty
        var opts = new RateLimitOptions();
        config.GetSection("RateLimits").Bind(opts);

        opts.LoginByIpPermitLimit.Should().Be(10);
        opts.CsrfByIpPermitLimit.Should().Be(60);
        opts.EmailByIpPermitLimit.Should().Be(10);
    }
}
```

- [ ] **Step 2: Run it, verify it FAILS (type not defined).**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~RateLimitOptionsDefaultsTests"`
Expected: FAIL — `RateLimitOptions` does not exist.

- [ ] **Step 3: Create `RateLimitOptions`.**

Create `ProjectCeres/Common/RateLimiting/RateLimitOptions.cs`:
```csharp
namespace ProjectCeres.Common.RateLimiting;

/// <summary>
/// Config-bound rate-limit permit counts. Defaults equal the pre-9.11 hardcoded
/// literals; only appsettings.E2E.json raises them so the E2E suite (one loopback
/// partition) doesn't 429. Windows stay hardcoded — only permit counts vary.
/// </summary>
public sealed class RateLimitOptions
{
    public int LoginByIpPermitLimit { get; set; } = 10;
    public int CsrfByIpPermitLimit { get; set; } = 60;
    public int EmailByIpPermitLimit { get; set; } = 10;
}
```

- [ ] **Step 4: Run the defaults test, verify it PASSES.**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~RateLimitOptionsDefaultsTests"`
Expected: PASS.

- [ ] **Step 5: Bind the options in `Program.cs` and replace the three literal permit counts.**

Immediately before `builder.Services.AddRateLimiter(options =>` (currently `Program.cs:281`), add:
```csharp
var rateLimitOptions = new ProjectCeres.Common.RateLimiting.RateLimitOptions();
builder.Configuration.GetSection("RateLimits").Bind(rateLimitOptions);
```
Then replace three literals inside the `AddRateLimiter` block (capture `rateLimitOptions` in the lambdas):
- In the `AuthLoginByIp` policy (currently `PermitLimit = 10,` at line 347): `PermitLimit = rateLimitOptions.LoginByIpPermitLimit,`
- In the `AuthCsrfByIp` policy (currently `PermitLimit = 60,` at line 359): `PermitLimit = rateLimitOptions.CsrfByIpPermitLimit,`
- In the **`GlobalLimiter`** lambda (currently `PermitLimit = 10,` at line 458 — this is the production EmailByIp enforcement point, NOT the named policy at line 475): `PermitLimit = rateLimitOptions.EmailByIpPermitLimit,`

Leave the named `EmailByIp` policy at line 470-480 as the hardcoded `10` — it is test-addressable only and not the production enforcement path; binding it would be a no-op (spec § 5.3).

- [ ] **Step 6: Build and run the full rate-limit + auth suites to confirm no behavior change at defaults.**

Run: `dotnet build ProjectCeres && dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~RateLimit"`
Expected: Build succeeded; all rate-limit tests PASS (defaults unchanged means existing limiter behavior is identical).

- [ ] **Step 7: Commit.**

```bash
git add ProjectCeres/Common/RateLimiting/RateLimitOptions.cs ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Configuration/RateLimitOptionsDefaultsTests.cs
git commit -m "feat(9.11): bind login/csrf/email-IP rate limits to config (defaults == prior literals)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: E2E database boot guard

**Files:**
- Create: `ProjectCeres/Common/E2eDatabaseGuardStartupCheck.cs`
- Modify: `ProjectCeres/Program.cs` (invoke under E2E only)

- [ ] **Step 1: Implement the guard (model on `RlsParityStartupCheck` — open a connection, read one scalar, throw).**

Create `ProjectCeres/Common/E2eDatabaseGuardStartupCheck.cs`:
```csharp
using Npgsql;

namespace ProjectCeres.Common;

/// <summary>
/// Refuses to start under ASPNETCORE_ENVIRONMENT=E2E unless the application connection
/// points at the dedicated <c>project_ceres_e2e</c> database. Collapses the env-flip
/// blast radius: a stray E2E env on a real host would otherwise file-sink auth tokens,
/// raise rate limits, and disable breached-password screening all at once.
/// </summary>
public static class E2eDatabaseGuardStartupCheck
{
    public const string ExpectedDatabase = "project_ceres_e2e";

    public static async Task EnsureConnectedToE2eDatabaseAsync(
        string applicationConnectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(applicationConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT current_database()";
        var actual = (string?)await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (!string.Equals(actual, ExpectedDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"E2E environment refuses to start: ApplicationConnection points at '{actual}', "
                + $"expected '{ExpectedDatabase}'. Refusing to risk a non-E2E database.");
    }
}
```

- [ ] **Step 2: Invoke it in `Program.cs` under E2E only.**

Inside the existing startup-check block (after `builder.Build()`, currently `Program.cs:540-553`), add an E2E branch. Place it just before the `if (!app.Configuration.GetValue<bool>("Stage75:SkipPrivilegeLeakCheck"))` block so it runs first:
```csharp
if (app.Environment.IsEnvironment("E2E"))
{
    var e2eConnection = app.Configuration.GetConnectionString("ApplicationConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:ApplicationConnection is not configured.");
    await E2eDatabaseGuardStartupCheck.EnsureConnectedToE2eDatabaseAsync(e2eConnection);
}
```

- [ ] **Step 3: Build.**

Run: `dotnet build ProjectCeres`
Expected: Build succeeded, 0 errors. (No unit test here — the guard needs a live non-E2E DB to exercise meaningfully; it is covered end-to-end when the wrapper script boots in Task 11. The logic is a single scalar compare.)

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres/Common/E2eDatabaseGuardStartupCheck.cs ProjectCeres/Program.cs
git commit -m "feat(9.11): E2E boot guard — refuse to start unless DB is project_ceres_e2e

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Wire FileSink + HIBP-stub E2E registration branches, with architecture pins

**Files:**
- Modify: `ProjectCeres/Program.cs` (email branch + HIBP branch)
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`
- Create: `ProjectCeres.Tests/Integration/Configuration/E2eEnvironmentRegistrationTests.cs`

- [ ] **Step 1: Write the failing architecture-test update — add `FileSinkEmailService` to knownImpls.**

Run: `grep -n "knownImpls\|IEmailService_impls" ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`
Open the `IEmailService_impls_are_LogOnly_Noop_or_Resend` test. It enumerates the concrete `IEmailService` implementations in the production assembly against a `knownImpls` set. Add `FileSinkEmailService` to that set (and, if the test name is asserted/used elsewhere, leave the name as-is — only the allowed-set changes). Example shape (match the real code):
```csharp
        var knownImpls = new[]
        {
            typeof(LogOnlyEmailService),
            typeof(NoopEmailService),
            typeof(ResendEmailService),
            typeof(FileSinkEmailService), // Stage 9.11 — E2E-only sink
        };
```

- [ ] **Step 2: Run the architecture test, verify it now FAILS for the opposite reason or PASSES.**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~ArchitectureTests.IEmailService_impls"`
Expected: PASS once `FileSinkEmailService` exists (Task 2 created it) and is in the set. If it FAILS saying the impl isn't registered/found, that confirms the test scans the assembly — good; the set edit fixes it.

- [ ] **Step 3: Write the failing E2E-registration DI tests.**

Create `ProjectCeres.Tests/Integration/Configuration/E2eEnvironmentRegistrationTests.cs`. These boot the real `Program` under `UseEnvironment("E2E")` and assert DI resolution. Use `WebApplicationFactory<Program>` with environment + config overrides. The key negative assertions: under E2E even **with** a Resend key set, `IEmailService` is `FileSinkEmailService`; and `IBreachedPasswordChecker` is the stub; while under Development neither is.
```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using Xunit;

namespace ProjectCeres.Tests.Integration.Configuration;

public class E2eEnvironmentRegistrationTests
{
    // Boot Program under E2E. We override the startup checks off (we are not testing
    // the DB guard here — that needs a live e2e DB; this test asserts DI wiring only)
    // and point connections at the existing test DB so Build() succeeds.
    private static WebApplicationFactory<Program> E2eFactory(bool withResendKey) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("E2E");
            b.UseSetting("Stage75:SkipPrivilegeLeakCheck", "true");
            b.UseSetting("E2E:SkipDatabaseGuard", "true"); // see Step 5 — guard honors this in tests
            b.UseSetting("ConnectionStrings:ApplicationConnection",
                "Host=localhost;Database=project_ceres_test;Username=ceres_app;Password=ceres_app_dev_password");
            b.UseSetting("ConnectionStrings:AdminConnection",
                "Host=localhost;Database=project_ceres_test;Username=ceres_admin;Password=ceres_admin_dev_password");
            b.UseSetting("Authentication:TokenLookupSecret:Secret",
                Convert.ToBase64String(new byte[32]));
            b.UseSetting("Email:FileSink:Directory",
                Path.Combine(Path.GetTempPath(), $"ceres-e2e-ditest-{Guid.NewGuid():N}"));
            if (withResendKey)
                b.UseSetting("Email:Resend:ApiKey", "re_test_key_should_be_ignored_under_e2e");
        });

    [Fact]
    public void Under_E2E_IEmailService_is_FileSink_even_with_a_Resend_key_present()
    {
        using var factory = E2eFactory(withResendKey: true);
        using var scope = factory.Services.CreateScope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        email.Should().BeOfType<FileSinkEmailService>();
    }

    [Fact]
    public void Under_E2E_IBreachedPasswordChecker_is_the_always_allow_stub()
    {
        using var factory = E2eFactory(withResendKey: false);
        using var scope = factory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IBreachedPasswordChecker>();
        checker.Should().BeOfType<AlwaysAllowBreachedPasswordChecker>();
    }
}
```

- [ ] **Step 4: Run, verify FAIL (E2E branches not wired; guard would also throw without the skip).**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~E2eEnvironmentRegistrationTests"`
Expected: FAIL — `IEmailService` resolves to `ResendEmailService` (key present) / `LogOnlyEmailService`, and `IBreachedPasswordChecker` resolves to `HaveIBeenPwnedPasswordChecker`.

- [ ] **Step 5: Wire the E2E email branch in `Program.cs`.**

In the email registration region (currently `Program.cs:170-193`), restructure so E2E wins regardless of the Resend key. Replace the existing `if (string.IsNullOrWhiteSpace(resendApiKey)) { ... } else { ... }` block with:
```csharp
if (builder.Environment.IsEnvironment("E2E"))
{
    // E2E: file-sink so Playwright can read verify/reset/unlock links. Keyed on
    // environment (not key-absence) so a machine-level Email__Resend__ApiKey can
    // never flip E2E to real sends. Singleton matches LogOnlyEmailService.
    var sinkDir = builder.Configuration["Email:FileSink:Directory"]
        ?? throw new InvalidOperationException("Email:FileSink:Directory is required under E2E.");
    builder.Services.AddSingleton<IEmailService>(sp =>
        new FileSinkEmailService(sinkDir,
            sp.GetRequiredService<ILogger<FileSinkEmailService>>()));
}
else if (string.IsNullOrWhiteSpace(resendApiKey))
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "Email:Resend:ApiKey is required in Production. " +
            "Set the Email__Resend__ApiKey environment variable.");
    }
    builder.Services.AddSingleton<IEmailService, LogOnlyEmailService>();
}
else
{
    builder.Services.AddResend(o => o.ApiToken = resendApiKey);
    builder.Services.AddScoped<IEmailService, ResendEmailService>();
}
```
Ensure `using Microsoft.Extensions.Logging;` is present (it almost certainly is). The Production fail-loud branch is preserved exactly.

- [ ] **Step 6: Wire the E2E HIBP-stub branch in `Program.cs`.**

The real checker is registered at `Program.cs:210`:
```csharp
builder.Services.AddHttpClient<IBreachedPasswordChecker, HaveIBeenPwnedPasswordChecker>();
```
Wrap it so E2E swaps the stub:
```csharp
if (builder.Environment.IsEnvironment("E2E"))
{
    builder.Services.AddSingleton<IBreachedPasswordChecker, AlwaysAllowBreachedPasswordChecker>();
}
else
{
    builder.Services.AddHttpClient<IBreachedPasswordChecker, HaveIBeenPwnedPasswordChecker>();
}
```

- [ ] **Step 7: Add the `E2E:SkipDatabaseGuard` test escape to the guard invocation in `Program.cs`.**

Update the Task-5 E2E guard block so it honors a test-only skip (the DI test boots E2E against `project_ceres_test`, not the e2e DB):
```csharp
if (app.Environment.IsEnvironment("E2E")
    && !app.Configuration.GetValue<bool>("E2E:SkipDatabaseGuard"))
{
    var e2eConnection = app.Configuration.GetConnectionString("ApplicationConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:ApplicationConnection is not configured.");
    await E2eDatabaseGuardStartupCheck.EnsureConnectedToE2eDatabaseAsync(e2eConnection);
}
```
The wrapper script (Task 11) never sets this flag, so the real run is always guarded.

- [ ] **Step 8: Run the registration + architecture tests, verify PASS.**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~E2eEnvironmentRegistrationTests|FullyQualifiedName~ArchitectureTests.IEmailService_impls"`
Expected: PASS.

- [ ] **Step 9: Run the full auth + architecture suite to confirm no regression in non-E2E environments.**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~Authentication|FullyQualifiedName~Architecture"`
Expected: PASS — Development/Test still resolve LogOnly/Hibp-stub-in-WAF as before (the WAF sets its own env, not E2E).

- [ ] **Step 10: Commit.**

```bash
git add ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs ProjectCeres.Tests/Integration/Configuration/E2eEnvironmentRegistrationTests.cs
git commit -m "feat(9.11): E2E-gated FileSink email + HIBP stub, pinned by architecture + DI tests

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: `appsettings.E2E.json` (tracked, secret-free)

**Files:**
- Create: `ProjectCeres/appsettings.E2E.json`
- Modify: `ProjectCeres/ProjectCeres.csproj` (ensure the file is copied to output if other appsettings are — check first)

- [ ] **Step 1: Confirm appsettings files flow to output (so the env file is picked up).**

Run: `grep -n "appsettings" ProjectCeres/ProjectCeres.csproj`
If `appsettings*.json` is already globbed into content/output (the SDK default copies `appsettings.json` and `appsettings.{Environment}.json`), no csproj change is needed. The .NET host loads `appsettings.E2E.json` automatically when `ASPNETCORE_ENVIRONMENT=E2E`. If a csproj entry explicitly lists appsettings files, add `appsettings.E2E.json` alongside.

- [ ] **Step 2: Create the file.**

Create `ProjectCeres/appsettings.E2E.json` (NO secrets — connection strings + TokenLookupSecret arrive via env vars from the wrapper script; this file is git-tracked and not covered by the Production/Staging gitignore rules):
```json
{
  "Vite": {
    "Base": "dist"
  },
  "Email": {
    "FileSink": {
      "Directory": ".e2e/emails"
    }
  },
  "RateLimits": {
    "LoginByIpPermitLimit": 1000,
    "CsrfByIpPermitLimit": 600,
    "EmailByIpPermitLimit": 1000
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```
Note: `Vite:Base = "dist"` tells Vite.AspNetCore to look for `wwwroot/dist/.vite/manifest.json`. `Email:FileSink:Directory` is relative to the app content root (resolved by the wrapper to an absolute path via env var if needed — see Task 11).

- [ ] **Step 3: Verify it parses and the host accepts E2E config (smoke — build only).**

Run: `dotnet build ProjectCeres`
Expected: Build succeeded. (Full boot under E2E is exercised in Task 11.)

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres/appsettings.E2E.json ProjectCeres/ProjectCeres.csproj
git commit -m "feat(9.11): appsettings.E2E.json — Vite:Base, email sink dir, raised rate limits (secret-free)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Phase 2 — SPA build path + wrapper script

### Task 8: Add the `.tsx` rollup inputs so the production manifest has the keys the Razor host looks up

**Files:**
- Modify: `ProjectCeres.Client/vite.config.ts` (rollupOptions.input + stale proxy port)
- Modify: `ProjectCeres.Client/scripts/check-bundle-size.mjs` (only if a new chunk trips a budget — check)

- [ ] **Step 1: Read the current rollup inputs and the Razor lookups.**

Run: `sed -n '44,60p' ProjectCeres.Client/vite.config.ts` and `grep -rn "vite-src" ProjectCeres/Views`
The Razor host (`Views/App/Index.cshtml`) looks up `src/app/main.tsx`; the error-page `_Layout` looks up `src/main.tsx`. Today's inputs are the three HTML files, so the manifest is keyed by `app.html`/`index.html` — neither `src/app/main.tsx` nor `src/main.tsx` is a key.

- [ ] **Step 2: Add the two `.tsx` entries as rollup inputs.**

In `vite.config.ts`, the `rollupOptions.input` block (lines 48-53) reads:
```ts
      input: {
        main: path.resolve(__dirname, 'index.html'),
        designSystem: path.resolve(__dirname, 'design-system.html'),
        app: path.resolve(__dirname, 'app.html'),
      },
```
Add the two source entries so the manifest contains `src/main.tsx` and `src/app/main.tsx` keys:
```ts
      input: {
        main: path.resolve(__dirname, 'index.html'),
        designSystem: path.resolve(__dirname, 'design-system.html'),
        app: path.resolve(__dirname, 'app.html'),
        // Stage 9.11 — the Razor host views resolve these via vite-src in non-Development
        // (manifest mode). Without them the manifest lacks the keys and the SPA host renders blank.
        razorAppEntry: path.resolve(__dirname, 'src/app/main.tsx'),
        razorLayoutEntry: path.resolve(__dirname, 'src/main.tsx'),
      },
```

- [ ] **Step 3: Fix the stale proxy port while in the file.**

Line 33 reads `target: 'https://localhost:7001',`. The Kestrel https dev port is 7081 (`launchSettings.json`). Change to:
```ts
        target: 'https://localhost:7081',
```

- [ ] **Step 4: Build and confirm the manifest now has both keys.**

Run: `pnpm --dir ProjectCeres.Client build`
Then: `grep -o '"src/app/main.tsx"\|"src/main.tsx"' ProjectCeres.Client/dist/.vite/manifest.json | sort -u`
Expected: both `"src/app/main.tsx"` and `"src/main.tsx"` printed. If the build fails the `check-size` step because a newly-named chunk exceeds budget, open `scripts/check-bundle-size.mjs`, find the budget map, and add/adjust the entry for the new chunk — do NOT raise an unrelated budget. If `check-size` passes, no change needed.

- [ ] **Step 5: Confirm vitest + tsc still pass (the new inputs don't affect them, but verify).**

Run: `pnpm --dir ProjectCeres.Client test`
Expected: PASS (161 files / 996 tests green).

- [ ] **Step 6: Commit.**

```bash
git add ProjectCeres.Client/vite.config.ts ProjectCeres.Client/scripts/check-bundle-size.mjs
git commit -m "build(9.11): add src/main.tsx + src/app/main.tsx rollup inputs for manifest-mode SPA serving

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: `tsconfig.e2e.json` so `e2e/*.ts` is type-checked

**Files:**
- Create: `ProjectCeres.Client/tsconfig.e2e.json`
- Modify: `ProjectCeres.Client/tsconfig.json` (add reference)

- [ ] **Step 1: Read the existing tsconfig structure.**

Run: `cat ProjectCeres.Client/tsconfig.json ProjectCeres.Client/tsconfig.app.json ProjectCeres.Client/tsconfig.node.json`
Note the solution-style `references` array in `tsconfig.json` and the `@/*` path alias in `tsconfig.app.json`.

- [ ] **Step 2: Create `tsconfig.e2e.json`.**

Create `ProjectCeres.Client/tsconfig.e2e.json` (mirror the compilerOptions style of `tsconfig.node.json`; include `e2e`, add node types and the `@/*` alias so suites can import SPA DTO types):
```json
{
  "compilerOptions": {
    "target": "ES2022",
    "lib": ["ES2023", "DOM"],
    "module": "ESNext",
    "moduleResolution": "bundler",
    "types": ["node"],
    "strict": true,
    "noEmit": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "baseUrl": ".",
    "paths": { "@/*": ["./src/*"] }
  },
  "include": ["e2e"]
}
```

- [ ] **Step 3: Reference it from `tsconfig.json`.**

In `tsconfig.json`, add `{ "path": "./tsconfig.e2e.json" }` to the `references` array so `tsc -b` (run by `pnpm build`) type-checks the e2e directory.

- [ ] **Step 4: Run the type-check; fix any pre-existing e2e type errors it surfaces.**

Run: `pnpm --dir ProjectCeres.Client exec tsc -b`
Expected: PASS. The existing `e2e/*.ts` files were never type-checked before — if `tsc` surfaces a genuine type error in `agent-walk.ts` / page objects, fix it minimally (do not loosen `strict`). If `@playwright/test` types aren't found, add `"@playwright/test"` is not needed in `types` (it's resolved via imports); ensure `skipLibCheck` is on (it is).

- [ ] **Step 5: Commit.**

```bash
git add ProjectCeres.Client/tsconfig.e2e.json ProjectCeres.Client/tsconfig.json
git commit -m "build(9.11): tsconfig.e2e.json — type-check e2e/*.ts via tsc -b

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 10: Playwright config split + `otpauth` devDep + scripts + gitignore

**Files:**
- Modify: `ProjectCeres.Client/e2e/playwright.config.ts` (narrow testMatch to agent-walk)
- Create: `ProjectCeres.Client/e2e/playwright.golden.config.ts`
- Modify: `ProjectCeres.Client/package.json` (scripts + `otpauth`)
- Modify: `ProjectCeres.Client/e2e/.gitignore`

- [ ] **Step 1: Read the current config + scripts.**

Run: `cat ProjectCeres.Client/e2e/playwright.config.ts` and `grep -n '"scripts"' -A 14 ProjectCeres.Client/package.json`
The existing config sets `testDir: '.'`, `testMatch: /.*\.spec\.ts$/`, `baseURL` from `process.env.APP_URL`, `workers: 1`, `ignoreHTTPSErrors: true`, no `webServer`, no `projects`. The `e2e` script is `playwright test --config e2e/playwright.config.ts`.

- [ ] **Step 2: Narrow the existing config so it only runs the agent-walk smoke.**

In `e2e/playwright.config.ts`, change `testMatch` from the broad `*.spec.ts` to exactly the agent-walk spec so the new golden suites don't get pulled into the APP_URL-only harness:
```ts
  testMatch: /agent-walk\.spec\.ts$/,
```
Leave everything else (baseURL from APP_URL, workers, ignoreHTTPSErrors) unchanged — the agent-walk env (`tools/agent-env/up.sh`) still drives it.

- [ ] **Step 3: Install `otpauth`.**

Run: `pnpm --dir ProjectCeres.Client add -D otpauth`
Expected: `otpauth` added to devDependencies.

- [ ] **Step 4: Create the golden config with a webServer pointing at the wrapper script.**

Create `ProjectCeres.Client/e2e/playwright.golden.config.ts`:
```ts
import { defineConfig, devices } from '@playwright/test'

const APP_URL = process.env.E2E_APP_URL ?? 'https://localhost:7299'

export default defineConfig({
  testDir: './auth',
  testMatch: /.*\.spec\.ts$/,
  // A flake is a failure until root-caused (docs/testing.md § Flaky tests).
  retries: 0,
  // Deterministic email-sink polling + a single shared loopback rate-limit partition.
  // Parallelism / sharding is Stage 16.16's job.
  workers: 1,
  fullyParallel: false,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['html', { outputFolder: '.artifacts/report', open: 'never' }]],
  outputDir: './.artifacts/test-results',
  use: {
    baseURL: APP_URL,
    ignoreHTTPSErrors: true, // dev self-signed cert
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],
  webServer: {
    command: 'bash ../tools/e2e/run-server.sh',
    url: `${APP_URL}/api/health`,
    timeout: 180_000, // build + migrate + boot
    reuseExistingServer: false,
    ignoreHTTPSErrors: true,
    stdout: 'pipe',
    stderr: 'pipe',
  },
})
```
Note: the `command` runs from `ProjectCeres.Client/` (Playwright's cwd), so `../tools/e2e/run-server.sh` resolves to repo-root `tools/e2e/run-server.sh`.

- [ ] **Step 5: Update package.json scripts.**

In `ProjectCeres.Client/package.json` scripts, repoint `e2e` to the golden config, add a UI variant, and add `e2e:walk` for the agent-walk harness:
```json
    "e2e": "playwright test --config e2e/playwright.golden.config.ts",
    "e2e:ui": "playwright test --config e2e/playwright.golden.config.ts --ui",
    "e2e:walk": "playwright test --config e2e/playwright.config.ts",
```
(Keep the existing `agent-walk` script if present — `e2e:walk` is the Playwright-config-driven sibling; the `tsx e2e/agent-walk.ts` script, if it exists, is separate and untouched.)

- [ ] **Step 6: Extend `e2e/.gitignore`.**

Append to `ProjectCeres.Client/e2e/.gitignore`:
```
.artifacts/
```
(The email sink lives at repo-root `.e2e/emails` per appsettings; ensure root `.gitignore` ignores `.e2e/` — add it in Task 11.)

- [ ] **Step 7: Confirm the golden config parses (it can't fully run until the wrapper + suites exist).**

Run: `pnpm --dir ProjectCeres.Client exec playwright test --config e2e/playwright.golden.config.ts --list`
Expected: either "no tests found" (auth/ is empty until Phase 3) or a clean parse with zero specs — NOT a config syntax error. The agent-walk config still lists its one spec: `pnpm --dir ProjectCeres.Client exec playwright test --config e2e/playwright.config.ts --list` should show `agent-walk.spec.ts`.

- [ ] **Step 8: Commit.**

```bash
git add ProjectCeres.Client/e2e/playwright.config.ts ProjectCeres.Client/e2e/playwright.golden.config.ts ProjectCeres.Client/package.json ProjectCeres.Client/pnpm-lock.yaml ProjectCeres.Client/e2e/.gitignore
git commit -m "build(9.11): split Playwright configs (agent-walk vs golden), add otpauth + e2e scripts

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 11: `tools/e2e/run-server.sh` wrapper (DB bootstrap + guarded wipe + bundle + boot)

**Files:**
- Create: `tools/e2e/run-server.sh` (chmod +x)
- Modify: root `.gitignore` (add `.e2e/`)

- [ ] **Step 1: Read the precedent scripts for exact invocations.**

Run: `cat tools/agent-env/up.sh` (already read — note the `dotnet ef database update --connection`, `--urls`, `/api/health` poll) and `sed -n '1,30p' scripts/setup-postgres-roles.sql` (note it's invoked with `-d <db>` and uses a `DBNAME` psql var or the connected DB).

- [ ] **Step 2: Write the wrapper script.**

Create `tools/e2e/run-server.sh`:
```bash
#!/usr/bin/env bash
# run-server.sh — Playwright webServer command for Stage 9.11 E2E.
# Bootstraps + migrates + wipes project_ceres_e2e, builds + stages the SPA bundle,
# asserts the manifest, then boots the app under ASPNETCORE_ENVIRONMENT=E2E over HTTPS.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DB="project_ceres_e2e"
APP_URL="${E2E_APP_URL:-https://localhost:7299}"
PORT="${APP_URL##*:}"

# Connection strings (dev role passwords mirror WafCollection.cs). These are dev-only
# credentials against a local Postgres; production uses a secrets store.
APP_CONN="Host=localhost;Database=${DB};Username=ceres_app;Password=ceres_app_dev_password"
ADMIN_CONN="Host=localhost;Database=${DB};Username=ceres_admin;Password=ceres_admin_dev_password"
MIGRATE_CONN="Host=localhost;Database=${DB};Username=ceres_migrator;Password=ceres_migrator_dev_password"

err() { echo "[e2e/run-server] $*" >&2; }

# 1. Create DB if missing (createdb is idempotent-guarded here).
if ! psql -lqt | cut -d '|' -f1 | grep -qw "$DB"; then
  err "creating database $DB"
  createdb "$DB"
fi

# 2. Provision roles/grants for THIS database (grants are per-database; roles are cluster-level).
err "applying setup-postgres-roles.sql to $DB"
psql -d "$DB" -v ON_ERROR_STOP=1 -f "$REPO_ROOT/scripts/setup-postgres-roles.sql" >/dev/null

# 3. Migrate as ceres_migrator (the --connection flag is mandatory — no design-time factory).
err "migrating $DB"
dotnet ef database update --project "$REPO_ROOT/ProjectCeres" \
  --context AppDbContext --connection "$MIGRATE_CONN" >/dev/null

# 4. Guarded wipe — assert the target DB before any DELETE.
ACTUAL_DB="$(psql -d "$DB" -tAc 'SELECT current_database()')"
if [[ "$ACTUAL_DB" != "$DB" ]]; then
  err "FATAL: wipe target is '$ACTUAL_DB', expected '$DB' — refusing to delete"
  exit 1
fi
err "wiping user + auth data (sparing lookup tables)"
psql -d "$DB" -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
DO $$
DECLARE
  spare text[] := ARRAY['AccountTypes','CategoryTypes','Currencies','ReportTypes','__EFMigrationsHistory'];
  r record;
BEGIN
  -- Disable triggers/FK ordering hassle: TRUNCATE all non-spared public tables in one shot.
  -- Run as a role with table ownership (ceres_migrator owns the schema's tables).
  FOR r IN
    SELECT tablename FROM pg_tables
    WHERE schemaname = 'public' AND tablename <> ALL(spare)
  LOOP
    EXECUTE format('TRUNCATE TABLE public.%I RESTART IDENTITY CASCADE', r.tablename);
  END LOOP;
END $$;
SQL
```
**Important:** the wipe `psql` connection must run as a role that owns the tables (TRUNCATE needs ownership). `ceres_migrator` owns them (it ran the migrations). Add `Username=ceres_migrator` to the psql call by exporting `PGUSER=ceres_migrator` for that one command, OR connect with `psql "postgresql://ceres_migrator:ceres_migrator_dev_password@localhost/${DB}"`. Use the URI form to be explicit. Continue the script:
```bash
# 5. Build the SPA and stage it under wwwroot/dist for manifest-mode serving.
err "building SPA bundle"
pnpm --dir "$REPO_ROOT/ProjectCeres.Client" build >/dev/null
DEST="$REPO_ROOT/ProjectCeres/wwwroot/dist"
rm -rf "$DEST"
mkdir -p "$DEST"
cp -R "$REPO_ROOT/ProjectCeres.Client/dist/." "$DEST/"

# 6. Assert the manifest has BOTH keys the Razor host views look up.
MANIFEST="$DEST/.vite/manifest.json"
for key in "src/app/main.tsx" "src/main.tsx"; do
  if ! grep -q "\"$key\"" "$MANIFEST"; then
    err "FATAL: manifest missing key '$key' — SPA host would render blank. See vite.config.ts rollup inputs."
    exit 1
  fi
done

# 7. Boot the app under E2E over HTTPS. Secrets via env vars (user-secrets do not load
# outside Development); appsettings.E2E.json carries the non-secret config.
EMAIL_SINK="$REPO_ROOT/.e2e/emails"
rm -rf "$EMAIL_SINK"; mkdir -p "$EMAIL_SINK"

cd "$REPO_ROOT"
export ASPNETCORE_ENVIRONMENT=E2E
export ConnectionStrings__ApplicationConnection="$APP_CONN"
export ConnectionStrings__AdminConnection="$ADMIN_CONN"
export Authentication__TokenLookupSecret__Secret="$(openssl rand -base64 32)"
export Email__FileSink__Directory="$EMAIL_SINK"
err "booting app on $APP_URL"
exec dotnet run --project "$REPO_ROOT/ProjectCeres" --no-launch-profile --urls "$APP_URL"
```

- [ ] **Step 3: Make it executable and ignore the artifact dirs.**

Run: `chmod +x tools/e2e/run-server.sh`
Append to root `.gitignore`:
```
# Stage 9.11 E2E artifacts
.e2e/
```
(`wwwroot/dist` and its `.vite/` are already gitignored per root `.gitignore:106,109`.)

- [ ] **Step 4: Smoke-test the wrapper standalone (boots, health-green, blocks).**

Prerequisite: local Postgres running, dev roles present (the same ones the integration suite uses). Run in one terminal:
`bash tools/e2e/run-server.sh`
In another terminal, once it prints "booting app", run:
`curl -sk https://localhost:7299/api/health -o /dev/null -w '%{http_code}\n'`
Expected: `200`. Then `curl -sk https://localhost:7299/app/login` and confirm the response body contains a `<script` tag referencing a hashed `/dist/assets/...js` (manifest-resolved), not a 404 or blank `#root`. Ctrl-C to stop. If the boot guard throws "expected project_ceres_e2e", the connection string is wrong; if the manifest assert fails, re-run Task 8.

- [ ] **Step 5: Commit.**

```bash
git add tools/e2e/run-server.sh .gitignore
git commit -m "build(9.11): tools/e2e/run-server.sh — bootstrap+migrate+guarded-wipe+bundle+boot wrapper

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Phase 3 — E2E support helpers + suites

> All suites run via `pnpm --dir ProjectCeres.Client e2e`. The webServer (Task 11) boots automatically. For a fast local loop use `--project=chromium`. EN strings below come from `src/app/i18n/locales/en.json`; assert by role/label/text, not by i18n key.

### Task 12: Support helpers (`e2e/support/`)

**Files:**
- Create: `ProjectCeres.Client/e2e/support/api.ts`
- Create: `ProjectCeres.Client/e2e/support/emails.ts`
- Create: `ProjectCeres.Client/e2e/support/totp.ts`
- Create: `ProjectCeres.Client/e2e/support/users.ts`

- [ ] **Step 1: Create `api.ts` — CSRF-aware request helper.**

Create `ProjectCeres.Client/e2e/support/api.ts`:
```ts
import type { APIRequestContext } from '@playwright/test'

// The CSRF request token comes from the GET /api/auth/csrf RESPONSE HEADER
// (X-XSRF-TOKEN), never the __Host-XSRF cookie value (they are a cryptographic
// pair). The pair rotates on login/logout, so re-fetch before each mutating call.
export async function csrfToken(request: APIRequestContext, baseURL: string): Promise<string> {
  const res = await request.get(`${baseURL}/api/auth/csrf`)
  const token = res.headers()['x-xsrf-token']
  if (!token) throw new Error('no X-XSRF-TOKEN header on /api/auth/csrf response')
  return token
}

export async function postJson(
  request: APIRequestContext,
  baseURL: string,
  path: string,
  body: unknown,
) {
  const token = await csrfToken(request, baseURL)
  return request.post(`${baseURL}${path}`, {
    headers: { 'X-XSRF-TOKEN': token, 'Content-Type': 'application/json' },
    data: body,
  })
}
```

- [ ] **Step 2: Create `emails.ts` — poll the file sink.**

The sink files land at repo-root `.e2e/emails` (set by the wrapper). From Playwright's cwd (`ProjectCeres.Client/`), that's `../.e2e/emails`. Create `ProjectCeres.Client/e2e/support/emails.ts`:
```ts
import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'

const SINK_DIR = path.resolve(__dirname, '../../../.e2e/emails')
const TOKEN_RE = /#token=([A-Za-z0-9_-]{43})/

interface SinkEmail { to: string; subject: string; bodyText: string; bodyHtml: string; sentAtUtc: string }

async function readAll(): Promise<SinkEmail[]> {
  let files: string[]
  try { files = await readdir(SINK_DIR) } catch { return [] }
  const json = files.filter((f) => f.endsWith('.json'))
  return Promise.all(
    json.map(async (f) => JSON.parse(await readFile(path.join(SINK_DIR, f), 'utf8')) as SinkEmail),
  )
}

// Poll for the newest email to `to` matching `predicate`; return it. Throws on timeout.
export async function waitForEmail(
  to: string,
  predicate: (e: SinkEmail) => boolean = () => true,
  timeoutMs = 15_000,
): Promise<SinkEmail> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const matches = (await readAll())
      .filter((e) => e.to.toLowerCase() === to.toLowerCase() && predicate(e))
      .sort((a, b) => b.sentAtUtc.localeCompare(a.sentAtUtc))
    if (matches.length > 0) return matches[0]
    await new Promise((r) => setTimeout(r, 250))
  }
  throw new Error(`no sink email to ${to} within ${timeoutMs}ms`)
}

export function extractToken(email: SinkEmail): string {
  const m = TOKEN_RE.exec(email.bodyText)
  if (!m) throw new Error(`no #token= in email body: ${email.bodyText.slice(0, 120)}`)
  return m[1]
}

// Build the SPA path a real user would land on after clicking the emailed link.
export function linkPath(email: SinkEmail): string {
  const m = /(https?:\/\/[^\s"]+#token=[A-Za-z0-9_-]{43})/.exec(email.bodyText)
  if (!m) throw new Error('no fragment URL in email body')
  return new URL(m[1]).pathname + new URL(m[1]).hash
}
```

- [ ] **Step 3: Create `totp.ts` — parse the otpauth URI and generate codes.**

Create `ProjectCeres.Client/e2e/support/totp.ts`:
```ts
import { URI, type TOTP } from 'otpauth'

// Parse the real otpauth:// URI returned by POST /api/auth/mfa/enroll so the helper
// uses the server's actual algorithm/digits/period rather than hardcoding them.
export function totpFromUri(otpauthUri: string): TOTP {
  return URI.parse(otpauthUri) as TOTP
}

export function code(totp: TOTP): string {
  return totp.generate()
}

// Wait until the current 30s window rolls to a fresh code distinct from `previous`
// (the server replay-guard rejects reuse within a window).
export async function nextDistinctCode(totp: TOTP, previous: string): Promise<string> {
  for (let i = 0; i < 35; i++) {
    const c = totp.generate()
    if (c !== previous) return c
    await new Promise((r) => setTimeout(r, 1000))
  }
  throw new Error('TOTP code did not roll within 35s')
}
```

- [ ] **Step 4: Create `users.ts` — API-driven verified-user factory.**

Create `ProjectCeres.Client/e2e/support/users.ts`:
```ts
import type { APIRequestContext } from '@playwright/test'
import { postJson } from './api'
import { waitForEmail, extractToken } from './emails'

export interface TestUser { email: string; password: string }

export function uniqueEmail(prefix = 'e2e'): string {
  return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}@e2e.local`
}

export const DEFAULT_PASSWORD = 'correct horse battery staple 9-11'

// Register via API, read the verify link from the sink, confirm via API.
// Uses the real server paths (not direct DB writes) — registration is prerequisite
// setup here, not the feature under test, so this is allowed per testing.md § Rules.
export async function createVerifiedUser(
  request: APIRequestContext,
  baseURL: string,
  user: TestUser = { email: uniqueEmail(), password: DEFAULT_PASSWORD },
): Promise<TestUser> {
  const reg = await postJson(request, baseURL, '/api/auth/register', {
    email: user.email,
    password: user.password,
  })
  if (reg.status() !== 204) throw new Error(`register failed: ${reg.status()} ${await reg.text()}`)

  const email = await waitForEmail(user.email, (e) => e.bodyText.includes('/app/email-verify#token='))
  const token = extractToken(email)

  const verify = await postJson(request, baseURL, '/api/auth/email/verify', { token })
  if (verify.status() !== 204) throw new Error(`verify failed: ${verify.status()} ${await verify.text()}`)
  return user
}
```

- [ ] **Step 5: Type-check the helpers.**

Run: `pnpm --dir ProjectCeres.Client exec tsc -b`
Expected: PASS. If `otpauth`'s `TOTP`/`URI` type names differ, run `grep -n "export" ProjectCeres.Client/node_modules/otpauth/dist/otpauth.d.ts | head -30` and adjust the import to the real exported names.

- [ ] **Step 6: Commit.**

```bash
git add ProjectCeres.Client/e2e/support/
git commit -m "feat(9.11): e2e support helpers — csrf, email sink, totp, verified-user factory

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 13: `register-login.spec.ts`

**Files:**
- Create: `ProjectCeres.Client/e2e/auth/register-login.spec.ts`

Selector map (EN):
- `/register` → heading "Create your account"; fields `#email`, `#password`; button "Create account"; success heading "Check your inbox".
- emailed link → `/app/email-verify#token=...`; verify page success heading "Email verified", link "Go to sign in".
- `/login` → heading "Sign in"; `#email`, `#password`; button "Sign in".
- success → SPA navigates into the app shell; assert `__Host-Session` cookie present.

- [ ] **Step 1: Write the suite.**

Create `ProjectCeres.Client/e2e/auth/register-login.spec.ts`:
```ts
import { test, expect } from '@playwright/test'
import { uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { waitForEmail, linkPath } from '../support/emails'

test('register → verify email → login → dashboard', async ({ page, context, baseURL }) => {
  const email = uniqueEmail('reglogin')

  // Register through the UI.
  await page.goto('/app/register')
  await expect(page.getByRole('heading', { name: 'Create your account' })).toBeVisible()
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(DEFAULT_PASSWORD)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Check your inbox' })).toBeVisible()

  // Follow the emailed verification link (root-path bug fixed in Task 1 → /app/email-verify).
  const mail = await waitForEmail(email, (e) => e.bodyText.includes('/app/email-verify#token='))
  await page.goto(linkPath(mail))
  await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible()

  // Sign in.
  await page.goto('/app/login')
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(DEFAULT_PASSWORD)
  await page.getByRole('button', { name: 'Sign in' }).click()

  // Landed in the app shell with a session cookie.
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })
  const cookies = await context.cookies()
  expect(cookies.some((c) => c.name === '__Host-Session')).toBe(true)
})
```

- [ ] **Step 2: Run it (Chromium only for speed).**

Run: `pnpm --dir ProjectCeres.Client e2e -- --project=chromium register-login`
Expected: PASS. If the post-login URL differs from `/app/`, open `src/app/pages/Login.tsx` to see where it navigates on success and adjust the `toHaveURL` regex. If the verify heading text differs, correct it from `en.json` (`auth.emailVerify.*`).

- [ ] **Step 3: Run across all three browsers.**

Run: `pnpm --dir ProjectCeres.Client e2e -- register-login`
Expected: PASS on chromium, firefox, webkit.

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres.Client/e2e/auth/register-login.spec.ts
git commit -m "test(9.11): register→verify→login golden-path E2E suite

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 14: `password-reset.spec.ts`

**Files:**
- Create: `ProjectCeres.Client/e2e/auth/password-reset.spec.ts`

Selector map: `/login` "Forgot password?" link → `/app/password-reset` heading "Reset your password", `#email`, button "Send reset link", success heading "Check your inbox". Emailed link → `/app/password-reset#token=` → heading "Choose a new password", `#new-password`, `#confirm-password`, button "Reset password". On success → `/login?reset=1` with toast "Password reset. Sign in with your new password."

- [ ] **Step 1: Write the suite.**

Create `ProjectCeres.Client/e2e/auth/password-reset.spec.ts`:
```ts
import { test, expect } from '@playwright/test'
import { createVerifiedUser, uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { waitForEmail, linkPath } from '../support/emails'

test('forgot password → reset link → new password → login', async ({ page, request, baseURL }) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('pwreset'),
    password: DEFAULT_PASSWORD,
  })
  const newPassword = 'a different long passphrase 9-11-x'

  await page.goto('/app/password-reset')
  await expect(page.getByRole('heading', { name: 'Reset your password' })).toBeVisible()
  await page.locator('#email').fill(user.email)
  await page.getByRole('button', { name: 'Send reset link' }).click()
  await expect(page.getByRole('heading', { name: 'Check your inbox' })).toBeVisible()

  const mail = await waitForEmail(user.email, (e) => e.bodyText.includes('/app/password-reset#token='))
  await page.goto(linkPath(mail))
  await expect(page.getByRole('heading', { name: 'Choose a new password' })).toBeVisible()
  await page.locator('#new-password').fill(newPassword)
  await page.locator('#confirm-password').fill(newPassword)
  await page.getByRole('button', { name: 'Reset password' }).click()

  // Redirects to /login?reset=1 with a toast.
  await expect(page).toHaveURL(/\/app\/login\?reset=1/, { timeout: 10_000 })
  await expect(page.getByText('Password reset. Sign in with your new password.')).toBeVisible()

  // The new password works.
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(newPassword)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })
})
```
(This user has no MFA, so the reset confirm does not ask for TOTP. MFA-path reset permutations stay in the integration suite per ADR-0071.)

- [ ] **Step 2: Run (chromium), fix selectors against `en.json` / page source if any text differs.**

Run: `pnpm --dir ProjectCeres.Client e2e -- --project=chromium password-reset`
Expected: PASS.

- [ ] **Step 3: Run all browsers.**

Run: `pnpm --dir ProjectCeres.Client e2e -- password-reset`
Expected: PASS.

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres.Client/e2e/auth/password-reset.spec.ts
git commit -m "test(9.11): password-reset golden-path E2E suite

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 15: `totp-enrol-and-first-login.spec.ts`

**Files:**
- Create: `ProjectCeres.Client/e2e/auth/totp-enrol-and-first-login.spec.ts`

Selector map: after login, `/app/security` heading "Security"; "Set up two-factor sign-in" button starts the wizard. Step 1: heading "Scan with your authenticator app", the otpauth URI is rendered (capture from the enroll API response via `page.waitForResponse('**/api/auth/mfa/enroll')`), InputOTP cells (`[data-slot="input-otp"] input` hidden input — `.fill(code)`), auto-submits on 6 digits. Step 2: heading "Save your backup codes", checkbox `aria-label="I've saved my backup codes somewhere safe"`, "Done" button (disabled until checked). Sign out → re-login → `200 {requiresTotp}` → `/app/login/totp` heading "Verify your identity", fill 6-digit code (auto-submits) → dashboard.

- [ ] **Step 1: Write the suite.**

Create `ProjectCeres.Client/e2e/auth/totp-enrol-and-first-login.spec.ts`:
```ts
import { test, expect } from '@playwright/test'
import { createVerifiedUser, uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { totpFromUri, code, nextDistinctCode } from '../support/totp'

test('enrol TOTP from Security → sign out → login with TOTP → dashboard', async ({ page, request, baseURL }) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('totp'),
    password: DEFAULT_PASSWORD,
  })

  // Log in (no MFA yet).
  await page.goto('/app/login')
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })

  // Start enrolment; capture the otpauth URI from the enroll API response.
  await page.goto('/app/security')
  const enrollResp = page.waitForResponse((r) => r.url().includes('/api/auth/mfa/enroll') && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Set up two-factor sign-in' }).click()
  const enroll = await enrollResp
  const { otpAuthUri } = await enroll.json()
  const totp = totpFromUri(otpAuthUri)

  // Step 1: enter a fresh code (auto-submits on 6 digits).
  await expect(page.getByRole('heading', { name: 'Scan with your authenticator app' })).toBeVisible()
  const enrollCode = code(totp)
  await page.locator('[data-slot="input-otp"] input').fill(enrollCode)

  // Step 2: save backup codes, confirm, Done.
  await expect(page.getByRole('heading', { name: 'Save your backup codes' })).toBeVisible()
  await page.getByLabel("I've saved my backup codes somewhere safe").check()
  await page.getByRole('button', { name: 'Done' }).click()

  // Sign out via the API helper path: navigate to logout is simplest via UI if present;
  // otherwise clear the session by going to /app/login after a logout request.
  await page.context().clearCookies()

  // Re-login → requiresTotp.
  await page.goto('/app/login')
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/login\/totp/, { timeout: 10_000 })
  await expect(page.getByRole('heading', { name: 'Verify your identity' })).toBeVisible()

  // Use a code distinct from the enrolment code (replay guard rejects reuse within a window).
  const loginCode = await nextDistinctCode(totp, enrollCode)
  await page.locator('[data-slot="input-otp"] input').fill(loginCode)

  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })
})
```

- [ ] **Step 2: Run (chromium). Watch for the 5-min half-auth TTL and the replay guard.**

Run: `pnpm --dir ProjectCeres.Client e2e -- --project=chromium totp-enrol`
Expected: PASS. If the enroll response field is not `otpAuthUri`, inspect it: `grep -rn "otpAuthUri\|manualEntryKey" ProjectCeres/Controllers/Api/MfaController.cs` and adjust. If filling the hidden OTP input doesn't trigger auto-submit, fall back to typing into the active slot with `page.locator('[data-slot="input-otp"] input').pressSequentially(loginCode)`.

- [ ] **Step 3: Run all browsers.**

Run: `pnpm --dir ProjectCeres.Client e2e -- totp-enrol`
Expected: PASS.

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres.Client/e2e/auth/totp-enrol-and-first-login.spec.ts
git commit -m "test(9.11): TOTP enrol + first-login-with-TOTP golden-path E2E suite

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 16: `lockout-self-service.spec.ts`

**Files:**
- Create: `ProjectCeres.Client/e2e/auth/lockout-self-service.spec.ts`

Selector map: 10 wrong-password submits on `/app/login` → account locked; the lockout email goes to the sink (issued on the transition). Emailed link → `/app/account/unlock#token=` → heading "Unlock your account", button "Unlock account" (button-press, not auto-confirm) → `/login?unlocked=1` with toast "Your account is unlocked. Sign in to continue."

- [ ] **Step 1: Write the suite.**

Create `ProjectCeres.Client/e2e/auth/lockout-self-service.spec.ts`:
```ts
import { test, expect } from '@playwright/test'
import { createVerifiedUser, uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { waitForEmail, linkPath } from '../support/emails'

test('10 wrong passwords → lockout email → unlock link → re-login', async ({ page, request, baseURL }) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('lockout'),
    password: DEFAULT_PASSWORD,
  })

  // Drive 10 failed logins through the form. The raised E2E AuthLoginByIp limit is what
  // lets all 10 reach the controller so the lockout TRANSITION (and its email) fires.
  await page.goto('/app/login')
  for (let i = 0; i < 10; i++) {
    await page.locator('#email').fill(user.email)
    await page.locator('#password').fill(`wrong-password-attempt-${i}-long-enough`)
    await page.getByRole('button', { name: 'Sign in' }).click()
    // Wait for the error to render before the next attempt (serial, deterministic).
    await page.waitForTimeout(150)
  }

  // Exactly one unlock email, with the /app-prefixed link (Task 1 fix).
  const mail = await waitForEmail(user.email, (e) => e.bodyText.includes('/app/account/unlock#token='))
  await page.goto(linkPath(mail))
  await expect(page.getByRole('heading', { name: 'Unlock your account' })).toBeVisible()
  await page.getByRole('button', { name: 'Unlock account' }).click()

  await expect(page).toHaveURL(/\/app\/login\?unlocked=1/, { timeout: 10_000 })
  await expect(page.getByText('Your account is unlocked. Sign in to continue.')).toBeVisible()

  // Correct password now works.
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })
})
```

- [ ] **Step 2: Run (chromium).**

Run: `pnpm --dir ProjectCeres.Client e2e -- --project=chromium lockout-self-service`
Expected: PASS. If no unlock email arrives, the lockout transition was starved — confirm `appsettings.E2E.json` raised `LoginByIpPermitLimit` (Task 7) and that Task 4's GlobalLimiter/login binding is live. If the unlock button text differs, correct from `en.json` (`auth.accountUnlock.*`).

- [ ] **Step 3: Run all browsers.**

Run: `pnpm --dir ProjectCeres.Client e2e -- lockout-self-service`
Expected: PASS.

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres.Client/e2e/auth/lockout-self-service.spec.ts
git commit -m "test(9.11): lockout self-service unlock golden-path E2E suite

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 17: `backup-code-recovery.spec.ts`

**Files:**
- Create: `ProjectCeres.Client/e2e/auth/backup-code-recovery.spec.ts`

Selector map: enrol TOTP via API (capture the returned backup codes), login → `/app/login/totp` → "Lost your device? Use a backup code" toggle → `#backup-code` field, "Verify" button → dashboard. Edge: re-login + reuse the same backup code → rejected (single-use). Enrolment via API requires `[RequireRecentAuth]` — a fresh login satisfies it; drive enroll + enroll/verify through `postJson`, using `totpFromUri` for the verify code.

- [ ] **Step 1: Write the suite.**

Create `ProjectCeres.Client/e2e/auth/backup-code-recovery.spec.ts`:
```ts
import { test, expect } from '@playwright/test'
import { createVerifiedUser, uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { postJson } from '../support/api'
import { totpFromUri, code, nextDistinctCode } from '../support/totp'

test('lost device → backup code login → dashboard; reuse rejected', async ({ page, request, baseURL }) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url, {
    email: uniqueEmail('backup'),
    password: DEFAULT_PASSWORD,
  })

  // Log in via API to get a session that satisfies [RequireRecentAuth], then enrol TOTP.
  const login = await postJson(request, url, '/api/auth/login', {
    email: user.email, password: user.password, rememberMe: false,
  })
  expect(login.status()).toBe(204)

  const enroll = await postJson(request, url, '/api/auth/mfa/enroll', {})
  expect(enroll.status()).toBe(200)
  const { otpAuthUri } = await enroll.json()
  const totp = totpFromUri(otpAuthUri)

  const verify = await postJson(request, url, '/api/auth/mfa/enroll/verify', { code: code(totp) })
  expect(verify.status()).toBe(200)
  const { backupCodes } = await verify.json()
  expect(Array.isArray(backupCodes)).toBe(true)
  expect(backupCodes.length).toBeGreaterThan(0)
  const backupCode: string = backupCodes[0]

  // Fresh browser session: log in via UI → TOTP challenge → switch to backup code.
  await page.context().clearCookies()
  await page.goto('/app/login')
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/login\/totp/, { timeout: 10_000 })

  await page.getByRole('button', { name: 'Lost your device? Use a backup code' }).click()
  await page.locator('#backup-code').fill(backupCode)
  await page.getByRole('button', { name: 'Verify' }).click()
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })

  // Edge: the same backup code is single-use — a second login with it is rejected.
  await page.context().clearCookies()
  await page.goto('/app/login')
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/login\/totp/, { timeout: 10_000 })
  await page.getByRole('button', { name: 'Lost your device? Use a backup code' }).click()
  await page.locator('#backup-code').fill(backupCode)
  await page.getByRole('button', { name: 'Verify' }).click()
  // Stays on the TOTP page with an error; does NOT reach the dashboard.
  await expect(page).toHaveURL(/\/app\/login\/totp/)
  await expect(page.getByText('Invalid code')).toBeVisible()
})
```

- [ ] **Step 2: Run (chromium).**

Run: `pnpm --dir ProjectCeres.Client e2e -- --project=chromium backup-code-recovery`
Expected: PASS. If the enroll/verify field names differ (`code`, `backupCodes`, `otpAuthUri`), confirm against `MfaController.cs` and adjust. If the backup-code error text differs, correct from `en.json` (`auth.totp.errors.invalid`).

- [ ] **Step 3: Run all browsers.**

Run: `pnpm --dir ProjectCeres.Client e2e -- backup-code-recovery`
Expected: PASS.

- [ ] **Step 4: Commit.**

```bash
git add ProjectCeres.Client/e2e/auth/backup-code-recovery.spec.ts
git commit -m "test(9.11): backup-code recovery golden-path E2E suite (single-use edge)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Phase 4 — Full-suite green + docs + close-out

### Task 18: Full E2E suite green across all browsers + agent-walk intact

**Files:** none (verification task)

- [ ] **Step 1: Run the complete golden suite across all three browsers from a clean DB.**

Run: `pnpm --dir ProjectCeres.Client e2e`
Expected: all five suites × 3 browsers PASS. The webServer wipes `project_ceres_e2e` at boot, so the run is repeatable. If any suite flakes, do NOT add a retry (testing.md § Flaky tests) — root-cause it (timing on a serial run usually means a missing `await expect(...).toBeVisible()` before the next action).

- [ ] **Step 2: Confirm the agent-walk harness still works.**

Run: `bash tools/agent-env/up.sh` then `APP_URL=<printed APP_URL> pnpm --dir ProjectCeres.Client e2e:walk` then `bash tools/agent-env/down.sh <printed SCHEMA>`
Expected: agent-walk.spec.ts PASS — the config split (Task 10) left it untouched.

- [ ] **Step 3: Run the full .NET + client suites (no regressions anywhere).**

Run: `dotnet build && dotnet test && pnpm --dir ProjectCeres.Client build && pnpm --dir ProjectCeres.Client test`
Expected: all exit 0; no test-count regression vs the build-matrix baseline (dotnet 1154 + analyzers 44; vitest 996).

- [ ] **Step 4: No commit (verification only). If any fix was needed, commit it with a `fix(9.11):` message.**

---

### Task 19: Docs + roadmap close-out

**Files:**
- Modify: `docs/testing.md`
- Modify: `docs/decisions/ADR-0071-e2e-testing-on-playwright.md`
- Modify: `docs/roadmap-phase-three.md`
- Modify: `docs/api-contract.md`

- [ ] **Step 1: Extend `docs/testing.md` § E2E with local-run docs.**

In the `### E2E Tool: Playwright (TypeScript)` subsection, add run instructions inline (there is no top-level § Running tests — run docs live per test-type, e.g. analyzer tests at line ~106):
- Prerequisites: local Postgres with the dev roles (same as the integration suite); the wrapper creates/migrates/wipes `project_ceres_e2e` automatically.
- Commands: `pnpm --dir ProjectCeres.Client e2e` (all 3 browsers), `... e2e -- --project=chromium` (fast loop), `... e2e:ui` (UI mode), `... e2e:walk` (agent-walk smoke).
- Artifacts: traces/screenshots/report under `ProjectCeres.Client/e2e/.artifacts/`; email sink at repo-root `.e2e/emails/`.
- `retries: 0` rationale (a flake is a failure until root-caused).
- When it runs: on demand + at auth-touching stage close-outs; CI wiring is Stage 16.16.
Replace the stale "Vite preview" wording (the .NET app serves the built bundle in manifest mode) and the "prerequisites do not exist until that phase" paragraph (E2E ships now, sans CI).

- [ ] **Step 2: Annotate ADR-0071's implementation gates.**

In `docs/decisions/ADR-0071-e2e-testing-on-playwright.md` § Implementation gates, mark the Stage 9.11 must-haves done: dep install + first config (shipped 2026-05-26 via 9.5a), `playwright.golden.config.ts` with `webServer` + browser matrix, `e2e/auth/register-login.spec.ts` golden path (+ the other four). Leave the Stage 16.16 CI items unchecked.

- [ ] **Step 3: Tick roadmap Stage 9.11 items + correct stale config lines.**

In `docs/roadmap-phase-three.md` § Stage 9.11, tick: install dep (note: pre-existing), `playwright.config.ts` (golden config), `e2e/` directory (note: extended), all five golden-path suites, real-Postgres fixture decision (dedicated `project_ceres_e2e`), local-run docs. Correct the line still promising `fullyParallel: true` + video-on-failure + `playwright-report/` to match what shipped (`workers: 1`, trace+screenshot retain-on-failure, `e2e/.artifacts/`), and the "Vite preview" line. Leave CI-in-16.16 deferral note as-is.

- [ ] **Step 4: Fix the register-failure status code in `docs/api-contract.md`.**

The contract claims Identity-policy register failures (short/breached password) return 422; tests pin **400** (RFC7807). Update `docs/api-contract.md` (and the inline comment at `AuthController.cs:109-112` if it makes the same wrong claim) so docs match the pinned behavior: DataAnnotations failures → 422 envelope; Identity-policy failures → 400 RFC7807.

- [ ] **Step 5: Run sync-docs and changelog-sync (stage close-out requires both to have fired this session — Phase E gate).**

Invoke the `sync-docs` skill against the full 9.11 diff, then `changelog-sync` to log the stage under `[Unreleased]`.

- [ ] **Step 6: Commit.**

```bash
git add docs/testing.md docs/decisions/ADR-0071-e2e-testing-on-playwright.md docs/roadmap-phase-three.md docs/api-contract.md CHANGELOG.md
git commit -m "docs(9.11): E2E local-run docs, ADR-0071 gates, roadmap ticks, api-contract 400 fix

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 7: Generate the evidence bundle + run the 3-reviewer pipeline for the final diff (the close-out commit touches auth + the suite); then the stage may close.**

Run: `tools/agent-env/build-matrix.sh 9.11`, regenerate turn-shape, dispatch reviewer-writer / reviewer-security / reviewer-playwright-test-audit against HEAD, write `reviewer-pipeline.json` with the new `diff_sha`. Confirm no surviving `block` verdict before ending the turn.

---

## Self-review (completed against the spec)

- **Spec § 3 (two URL fixes):** Task 1 — tests-first, both assertions + both services. ✓
- **Spec § 4 (E2E server env / wrapper):** Task 11 (DB bootstrap + guarded wipe + bundle + manifest assert + boot), Task 7 (appsettings), Task 5+6 (boot guard incl. test escape). ✓
- **Spec § 5.2 (FileSink + pins):** Task 2 (service + shape test), Task 6 (E2E branch + architecture knownImpls + DI resolution-even-with-key test). ✓
- **Spec § 5.3 (rate-limit binding + pins):** Task 4 (RateLimitOptions, GlobalLimiter lambda bound, defaults==literals test, raised-only-in-E2E via appsettings). ✓
- **Spec § 5.4 (HIBP stub + pin):** Task 3 (stub), Task 6 (E2E branch + DI pin). ✓
- **Spec § 5.5 (manifest keys):** Task 8 (rollup inputs), Task 11 step 6 (manifest dual-key abort). ✓
- **Spec § 5.7 (boot guard):** Task 5. ✓
- **Spec § 6 (config split, tsconfig.e2e, otpauth, retries:0, artifacts):** Tasks 9, 10. ✓
- **Spec § 7 (helpers + 5 suites):** Tasks 12–17. ✓
- **Spec § 8 (docs):** Task 19. ✓
- **Spec § 10 (ship gates):** Task 18 (all-browser green + agent-walk intact + full suites), Task 19 step 7 (evidence + reviewers). ✓
- **Type consistency:** `createVerifiedUser`, `postJson`, `csrfToken`, `waitForEmail`, `extractToken`, `linkPath`, `totpFromUri`, `code`, `nextDistinctCode` are defined once (Task 12) and used with matching signatures in Tasks 13–17. `RateLimitOptions` properties (`LoginByIpPermitLimit`/`CsrfByIpPermitLimit`/`EmailByIpPermitLimit`) match between Task 4 definition, the appsettings keys (Task 7), and the binding (Task 4 step 5). ✓
- **Placeholder scan:** no TBD/TODO; every code step shows code; commands have expected output. ✓
- **Note carried to execution:** several suite selectors (post-login URL, exact heading/toast/error EN strings, MFA enroll response field names) are pinned to the selector map but flagged with a "if it differs, correct from en.json / controller" fallback in the run step — because the SPA source is the source of truth and the plan author read a summary, not every line. The run-then-fix loop in each suite task absorbs any drift.
