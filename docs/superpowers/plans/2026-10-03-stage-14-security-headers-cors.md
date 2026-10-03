# Stage 14 — HTTP Security Headers + CORS Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every HTTP response carries the standard hardening headers (hash-based CSP, nosniff, frame-options, referrer-policy, permissions-policy, COOP/CORP), authenticated responses are uncacheable, forwarded-headers is hardened against IP spoofing, and CORS is wired (inert under same-origin) — without pre-committing Stage 16 hosting decisions.

**Architecture:** Add middleware in `Program.cs` at three order-critical points: forwarded-headers first, security headers early (before static files), CORS after routing. Use `NetEscapades.AspNetCore.SecurityHeaders`. Add a `/api/csp-report` endpoint with a dedicated by-IP rate-limit policy. Compute CSP script hashes at build time so they never drift. All new config lives in new `appsettings.json` sections bound to options types.

**Tech Stack:** .NET 10, ASP.NET Core, `NetEscapades.AspNetCore.SecurityHeaders`, xUnit + WebApplicationFactory integration tests, Vite build for the hash step.

**Spec:** `docs/superpowers/specs/2026-10-03-stage-14-security-headers-cors-design.md`

## Global Constraints

- **SDK pinned:** 10.0.401 via `global.json` (`rollForward: latestPatch`). Use `dotnet` from `~/.dotnet` locally.
- **New dependency must clear the vuln-scan gate:** `NetEscapades.AspNetCore.SecurityHeaders` must pass `dotnet list package --vulnerable --include-transitive` (the `repo-hygiene` CI gate fails on any finding). Verify before committing Task 1.
- **CSP is hash-based, never nonce-based.** `script-src` must NEVER contain `'unsafe-inline'`. (Static-SPA + industry standard — see spec.)
- **No derived-column / no-BLOB / deletion rules** per CLAUDE.md — not touched by this stage, but the standing rules apply.
- **Status codes:** this project returns 422 (not 400) for model-validation via `InvalidModelStateResponseFactory`. The `/csp-report` endpoint returns 204.
- **Tests filter by test-owned data; never run `dotnet test` concurrently** (shared test DB). The Stop hook runs the suite; run targeted filters only.
- **No `Co-Authored-By` trailer in commits. Stay on `main`.**
- **Env caveat:** WAF boots under `Testing`/`Development` where `UseHsts` is skipped and cookie-secure is relaxed — header tests assert only environment-appropriate headers.

---

## File Structure

- Create: `ProjectCeres/Common/Security/SecurityHeadersConfig.cs` — options types + the policy-builder extension (`AddCeresSecurityHeaders`, `UseCeresSecurityHeaders`).
- Create: `ProjectCeres/Common/Security/CacheHeaderMiddleware.cs` — path-aware cache headers + `Clear-Site-Data` on logout.
- Create: `ProjectCeres/Controllers/Api/CspReportApiController.cs` — `POST /api/csp-report`.
- Modify: `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs` — add `CspReportByIp` constant.
- Modify: `ProjectCeres/Program.cs` — register options, forwarded-headers (first), security headers (early), CORS (after routing), cache middleware, the rate-limit policy.
- Modify: `ProjectCeres/appsettings.json` — add `SecurityHeaders`, `Cors`, `ForwardedHeaders` sections.
- Create: `ProjectCeres.Client/scripts/compute-csp-hashes.mjs` — build-time hash of the inline shell scripts, emitted to a file the server reads.
- Modify: `ProjectCeres.Client/package.json` — wire the hash script into `build`.
- Modify: `ProjectCeres.Client/src/components/ui/chart.tsx` — documented `react/no-danger` exception (false-positive, per spec).
- Create: `ProjectCeres.Tests/Integration/Security/SecurityHeadersTests.cs` — the header/CSP/cache/CORS assertions.
- Create: `ProjectCeres.Tests/Integration/Security/ForwardedHeadersTests.cs` — the spoof-rejection negative test.
- Create: `ProjectCeres.Tests/Integration/Security/CspReportEndpointTests.cs` — the endpoint test.
- Modify: `docs/security-model.md` — CSP skeleton nonce→hash correction + the `react/no-danger` exception entry (via `sync-docs` at stage close).

---

## Task 1: Add the NetEscapades dependency and verify it's clean

**Files:**
- Modify: `ProjectCeres/ProjectCeres.csproj`

**Interfaces:**
- Produces: the `NetEscapades.AspNetCore.SecurityHeaders` package reference available to all later tasks.

- [ ] **Step 1: Add the package reference**

Run:
```bash
cd /Users/marcellhernandez/Code/dotnet/project-ceres
~/.dotnet/dotnet add ProjectCeres/ProjectCeres.csproj package NetEscapades.AspNetCore.SecurityHeaders
```

- [ ] **Step 2: Verify it clears the vuln-scan gate (the CI gate)**

Run:
```bash
~/.dotnet/dotnet list ProjectCeres/ProjectCeres.csproj package --vulnerable --include-transitive
```
Expected: output contains `has no vulnerable packages` (if a vulnerability is reported, STOP — do not proceed; the `repo-hygiene` CI gate will fail. Report back.)

- [ ] **Step 3: Verify the solution still builds**

Run: `~/.dotnet/dotnet build ProjectCeres/ProjectCeres.csproj -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/ProjectCeres.csproj ProjectCeres/packages.lock.json 2>/dev/null; git add ProjectCeres/ProjectCeres.csproj
git commit -m "build(14): add NetEscapades.AspNetCore.SecurityHeaders"
```

---

## Task 2: Build-time CSP hash computation for the inline shell scripts

**Files:**
- Create: `ProjectCeres.Client/scripts/compute-csp-hashes.mjs`
- Modify: `ProjectCeres.Client/package.json`
- Test: manual verification (build output), asserted later in Task 7's drift-guard test.

**Interfaces:**
- Produces: `ProjectCeres/wwwroot/dist/csp-hashes.json` — a JSON array of `sha256-...` strings for the inline `<script>` blocks in the built `dist/app.html`. The server reads this at startup (Task 3).

- [ ] **Step 1: Write the hash-computation script**

Create `ProjectCeres.Client/scripts/compute-csp-hashes.mjs`:
```js
// Computes sha256 CSP hashes of every inline <script> in the built app.html
// and writes them to csp-hashes.json next to it. Run AFTER vite build.
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const distDir = resolve(import.meta.dirname, '../../ProjectCeres/wwwroot/dist');
const html = readFileSync(resolve(distDir, 'app.html'), 'utf8');

// Match inline <script> blocks only (no src attribute).
const hashes = [];
const re = /<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/g;
let m;
while ((m = re.exec(html)) !== null) {
  const body = m[1];
  const digest = createHash('sha256').update(body, 'utf8').digest('base64');
  hashes.push(`sha256-${digest}`);
}

writeFileSync(resolve(distDir, 'csp-hashes.json'), JSON.stringify(hashes, null, 2));
console.log(`compute-csp-hashes: wrote ${hashes.length} hash(es) to dist/csp-hashes.json`);
```

- [ ] **Step 2: Wire it into the client build**

In `ProjectCeres.Client/package.json`, change the `build` script so the hash step runs after the vite build and the bundle-size check. If `build` is currently:
```json
"build": "tsc -b && vite build && npm run check-size"
```
change it to:
```json
"build": "tsc -b && vite build && node scripts/compute-csp-hashes.mjs && pnpm run check-size"
```
(Keep whatever the existing pre/post steps are; only insert `&& node scripts/compute-csp-hashes.mjs` immediately after `vite build`. Read the current `build` line first and preserve it.)

- [ ] **Step 3: Run the build and verify the hash file appears**

Run:
```bash
pnpm --dir ProjectCeres.Client build
tools/stage-spa.sh   # stages dist into wwwroot/dist for the non-Vite path
cat ProjectCeres/wwwroot/dist/csp-hashes.json
```
Expected: a JSON array with exactly 2 `sha256-...` entries (the theme-init + sidebar bootstrap scripts).

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/scripts/compute-csp-hashes.mjs ProjectCeres.Client/package.json
git commit -m "build(14): compute CSP script hashes at build time"
```

---

## Task 3: Security-headers options + policy builder, wired into the pipeline

**Files:**
- Create: `ProjectCeres/Common/Security/SecurityHeadersConfig.cs`
- Modify: `ProjectCeres/appsettings.json`
- Modify: `ProjectCeres/Program.cs` (register + `UseSecurityHeaders` after line 1048, before `UseStaticFiles` at 1049)
- Test: `ProjectCeres.Tests/Integration/Security/SecurityHeadersTests.cs` (first assertions)

**Interfaces:**
- Consumes: `csp-hashes.json` from Task 2.
- Produces: `app.UseCeresSecurityHeaders()` extension; `SecurityHeaders` config section.

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Integration/Security/SecurityHeadersTests.cs`:
```csharp
using System.Net.Http;
using FluentAssertions;
using ProjectCeres.Tests.Common;
using Xunit;

namespace ProjectCeres.Tests.Integration.Security;

public class SecurityHeadersTests : IntegrationTestBase<Bucket1Factory>
{
    private readonly HttpClient _client;
    public SecurityHeadersTests(Bucket1Factory f, Bucket1Database db) : base(f, db)
        => _client = f.CreateClient();

    [Fact]
    public async Task Response_carries_the_static_hardening_headers()
    {
        var res = await _client.GetAsync("/api/health");
        res.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        res.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        res.Headers.GetValues("Referrer-Policy").Should().Contain("strict-origin-when-cross-origin");
        res.Headers.Contains("Cross-Origin-Opener-Policy").Should().BeTrue();
    }

    [Fact]
    public async Task Csp_is_present_and_script_src_is_not_unsafe_inline()
    {
        var res = await _client.GetAsync("/api/health");
        var csp = string.Join(" ", res.Headers.GetValues("Content-Security-Policy"));
        csp.Should().Contain("default-src 'self'");
        csp.Should().NotContain("'unsafe-inline'"); // strict property on script-src
        csp.Should().Contain("object-src 'none'");
    }
}
```
(Confirm the exact base-class + fixture names by reading an existing file in `ProjectCeres.Tests/Integration/` — e.g. `MovementsBulkClearedTests.cs` uses `IntegrationTestBase<Bucket1Factory>` + `Bucket1Database`. Match whatever that file does, including the `[Collection(...)]` attribute.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SecurityHeadersTests"`
Expected: FAIL (headers absent).

- [ ] **Step 3: Write the options + policy builder**

Create `ProjectCeres/Common/Security/SecurityHeadersConfig.cs`:
```csharp
using System.Text.Json;
using NetEscapades.AspNetCore.SecurityHeaders;

namespace ProjectCeres.Common.Security;

/// <summary>Builds the Ceres security-header policy. CSP is hash-based (static SPA).</summary>
public static class SecurityHeadersConfig
{
    public static HeaderPolicyCollection Build(IWebHostEnvironment env, string distDir)
    {
        var scriptHashes = LoadScriptHashes(distDir);

        var policy = new HeaderPolicyCollection()
            .AddContentTypeOptionsNoSniff()
            .AddFrameOptionsDeny()
            .AddReferrerPolicyStrictOriginWhenCrossOrigin()
            .AddCrossOriginOpenerPolicy(b => b.SameOrigin())
            .AddCrossOriginResourcePolicy(b => b.SameOrigin())
            .AddPermissionsPolicy(b =>
            {
                b.AddCamera().None();
                b.AddMicrophone().None();
                b.AddGeolocation().None();
            });

        policy.AddContentSecurityPolicy(csp =>
        {
            csp.AddDefaultSrc().Self();
            var script = csp.AddScriptSrc().Self();
            foreach (var h in scriptHashes) script.WithHash256(StripPrefix(h));
            csp.AddStyleSrc().Self().UnsafeInline(); // see spec: recharts injects inline <style>; styles only
            csp.AddImgSrc().Self().Data();
            csp.AddFontSrc().Self();
            csp.AddConnectSrc().Self();
            csp.AddObjectSrc().None();
            csp.AddBaseUri().Self();
            csp.AddFrameAncestors().None();
            csp.AddReportUri().To("/api/csp-report");
        });

        return policy;
    }

    private static string StripPrefix(string h) => h.StartsWith("sha256-") ? h["sha256-".Length..] : h;

    private static IReadOnlyList<string> LoadScriptHashes(string distDir)
    {
        var path = Path.Combine(distDir, "csp-hashes.json");
        if (!File.Exists(path)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }
}
```
(The `NetEscapades` fluent API names — `WithHash256`, `AddReportUri().To(...)` — must be confirmed against the installed package version; read the package's XML docs / IntelliSense if a name differs, and use the equivalent. The SHAPE is fixed by the spec; the exact method names are the package's.)

- [ ] **Step 4: Add the config section**

In `ProjectCeres/appsettings.json`, add a top-level section:
```json
"SecurityHeaders": {
  "Enabled": true
}
```

- [ ] **Step 5: Wire into Program.cs**

After `app.UseHttpsRedirection();` (line ~1048) and BEFORE `app.UseStaticFiles();` (line ~1049), insert:
```csharp
// Security headers (Stage 14): hash-based CSP + hardening headers. Placed before
// UseStaticFiles so static responses are covered too.
var distDir = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "dist");
app.UseSecurityHeaders(ProjectCeres.Common.Security.SecurityHeadersConfig.Build(app.Environment, distDir));
```

- [ ] **Step 6: Build the SPA so csp-hashes.json exists for the test host**

Run: `pnpm --dir ProjectCeres.Client build && tools/stage-spa.sh`

- [ ] **Step 7: Run the test to verify it passes**

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SecurityHeadersTests"`
Expected: PASS (both tests).

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres/Common/Security/SecurityHeadersConfig.cs ProjectCeres/appsettings.json ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Security/SecurityHeadersTests.cs
git commit -m "feat(14): hash-based CSP + hardening security headers"
```

---

## Task 4: /api/csp-report endpoint + dedicated rate-limit policy

**Files:**
- Create: `ProjectCeres/Controllers/Api/CspReportApiController.cs`
- Modify: `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs` (add `CspReportByIp`)
- Modify: `ProjectCeres/Program.cs` (register the policy, mirroring `AuthCsrfByIp`)
- Test: `ProjectCeres.Tests/Integration/Security/CspReportEndpointTests.cs`

**Interfaces:**
- Consumes: `AuthRateLimitPolicies.CspReportByIp` (new constant).
- Produces: `POST /api/csp-report` → 204.

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Integration/Security/CspReportEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using ProjectCeres.Tests.Common;
using Xunit;

namespace ProjectCeres.Tests.Integration.Security;

public class CspReportEndpointTests : IntegrationTestBase<Bucket1Factory>
{
    private readonly HttpClient _client;
    public CspReportEndpointTests(Bucket1Factory f, Bucket1Database db) : base(f, db)
        => _client = f.CreateClient();

    [Fact]
    public async Task Anonymous_post_is_accepted_with_204()
    {
        var body = new { cspReport = new { violatedDirective = "script-src" } };
        var res = await _client.PostAsJsonAsync("/api/csp-report", body);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~CspReportEndpointTests"`
Expected: FAIL (404).

- [ ] **Step 3: Add the policy-name constant**

In `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs`, add alongside the existing constants:
```csharp
/// <summary>By-IP limiter for the unauthenticated public /api/csp-report ingest.</summary>
public const string CspReportByIp = "csp-report-by-ip";
```

- [ ] **Step 4: Register the policy in Program.cs**

In the `AddRateLimiter` options block (near the other `options.AddPolicy(AuthRateLimitPolicies.AuthCsrfByIp, ...)` call ~line 750), add a by-IP fixed-window policy mirroring `AuthCsrfByIp`'s shape (read that call and copy its partition/limit structure; a modest limit like 30/min is appropriate for violation reports):
```csharp
options.AddPolicy(AuthRateLimitPolicies.CspReportByIp, httpContext =>
    RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
```
(Match the exact namespaces/using already present for the other policies.)

- [ ] **Step 5: Write the controller**

Create `ProjectCeres/Controllers/Api/CspReportApiController.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/csp-report")]
public class CspReportApiController(ILogger<CspReportApiController> logger) : ControllerBase
{
    [HttpPost, AllowAnonymous, EnableRateLimiting(AuthRateLimitPolicies.CspReportByIp)]
    public IActionResult Report([FromBody] object? report)
    {
        logger.LogWarning("CSP violation report: {Report}", report);
        return NoContent();
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~CspReportEndpointTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Controllers/Api/CspReportApiController.cs ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Security/CspReportEndpointTests.cs
git commit -m "feat(14): /api/csp-report endpoint with by-IP rate limit"
```

---

## Task 5: Path-aware cache headers + Clear-Site-Data on logout

**Files:**
- Create: `ProjectCeres/Common/Security/CacheHeaderMiddleware.cs`
- Modify: `ProjectCeres/Program.cs` (register after routing/auth so it can see the endpoint + user)
- Test: add to `ProjectCeres.Tests/Integration/Security/SecurityHeadersTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `app.UseCeresCacheHeaders()`.

- [ ] **Step 1: Write the failing test** (append to `SecurityHeadersTests`)

```csharp
    [Fact]
    public async Task Api_response_is_not_cacheable()
    {
        var res = await _client.GetAsync("/api/health");
        var cc = string.Join(" ", res.Headers.TryGetValues("Cache-Control", out var v) ? v : new[] { "" });
        cc.Should().Contain("no-store");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SecurityHeadersTests.Api_response_is_not_cacheable"`
Expected: FAIL.

- [ ] **Step 3: Write the middleware**

Create `ProjectCeres/Common/Security/CacheHeaderMiddleware.cs`:
```csharp
using Microsoft.Extensions.Primitives;

namespace ProjectCeres.Common.Security;

/// <summary>Path-aware cache headers (Stage 14.8). Static /dist/* keeps its long cache;
/// API + authenticated responses are no-store; logout additionally clears site data.</summary>
public sealed class CacheHeaderMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.OnStarting(() =>
        {
            var path = ctx.Request.Path;
            if (path.StartsWithSegments("/dist"))
                return Task.CompletedTask; // hashed assets: leave the static-files cache header

            if (path.StartsWithSegments("/api") || ctx.User.Identity?.IsAuthenticated == true)
                ctx.Response.Headers.CacheControl = "private, no-store";

            if (path.StartsWithSegments("/health"))
                ctx.Response.Headers.CacheControl = "no-store";

            if (path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
                ctx.Response.Headers["Clear-Site-Data"] = new StringValues("\"cache\", \"cookies\", \"storage\"");

            return Task.CompletedTask;
        });
        await next(ctx);
    }
}
```

- [ ] **Step 4: Register in Program.cs**

After `app.UseAuthorization();` (~line 1147) so `ctx.User` is populated, and before `app.MapControllers();`:
```csharp
app.UseMiddleware<ProjectCeres.Common.Security.CacheHeaderMiddleware>();
```

- [ ] **Step 5: Run to verify it passes**

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SecurityHeadersTests"`
Expected: PASS (all).

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Common/Security/CacheHeaderMiddleware.cs ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Security/SecurityHeadersTests.cs
git commit -m "feat(14): path-aware cache headers + Clear-Site-Data on logout"
```

---

## Task 6: Forwarded-headers middleware (first) + spoof-rejection test

**Files:**
- Modify: `ProjectCeres/appsettings.json` (`ForwardedHeaders` section)
- Modify: `ProjectCeres/Program.cs` (register `UseForwardedHeaders` FIRST, right after `var app = builder.Build();` line 997)
- Test: `ProjectCeres.Tests/Integration/Security/ForwardedHeadersTests.cs`

**Interfaces:**
- Produces: `ForwardedHeaders` config section (empty `KnownProxies` today).

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Tests/Integration/Security/ForwardedHeadersTests.cs`:
```csharp
using System.Net.Http;
using FluentAssertions;
using ProjectCeres.Tests.Common;
using Xunit;

namespace ProjectCeres.Tests.Integration.Security;

public class ForwardedHeadersTests : IntegrationTestBase<Bucket1Factory>
{
    private readonly HttpClient _client;
    public ForwardedHeadersTests(Bucket1Factory f, Bucket1Database db) : base(f, db)
        => _client = f.CreateClient();

    // With an empty KnownProxies list, a spoofed X-Forwarded-For must NOT be
    // honoured — the endpoint echoes the connection remote IP, which must not
    // become the spoofed value. (Needs the /api/health/whoami helper below, or
    // assert via an existing endpoint that reflects RemoteIpAddress.)
    [Fact]
    public async Task Spoofed_x_forwarded_for_does_not_change_remote_ip()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/health/remote-ip");
        req.Headers.Add("X-Forwarded-For", "203.0.113.7");
        var res = await _client.GetAsync(req.RequestUri);
        var ip = await (await _client.SendAsync(req)).Content.ReadAsStringAsync();
        ip.Should().NotContain("203.0.113.7");
    }
}
```
(This needs an endpoint that reflects `RemoteIpAddress`. Add a minimal `[HttpGet("remote-ip"), AllowAnonymous]` action to `HealthApiController` returning `HttpContext.Connection.RemoteIpAddress?.ToString()`, guarded to non-Production, OR assert the property directly via a custom middleware probe in the WAF. Prefer the tiny health action — it mirrors the existing `HealthApiController` shape.)

- [ ] **Step 2: Run to verify it fails**

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ForwardedHeadersTests"`
Expected: FAIL (endpoint missing, or IP honoured).

- [ ] **Step 3: Add the config section**

In `ProjectCeres/appsettings.json`:
```json
"ForwardedHeaders": {
  "KnownProxies": []
}
```

- [ ] **Step 4: Register UseForwardedHeaders first**

Immediately after `var app = builder.Build();` (line 997), before everything else:
```csharp
// Forwarded-headers (Stage 14.7): MUST be first so every downstream component
// sees the real client IP. KnownProxies is empty today — on .NET 10 this fails
// CLOSED (forwarded headers ignored) until Stage 16 populates it.
// FIXME(Stage 16): populate ForwardedHeaders:KnownProxies with the chosen proxy.
var fhOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
};
foreach (var ip in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    fhOptions.KnownProxies.Add(System.Net.IPAddress.Parse(ip));
app.UseForwardedHeaders(fhOptions);
```

- [ ] **Step 5: Add the remote-ip health action** (per Step 1 note)

In `HealthApiController`:
```csharp
[HttpGet("remote-ip"), AllowAnonymous]
public IActionResult RemoteIp() => Content(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
```

- [ ] **Step 6: Run to verify it passes**

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ForwardedHeadersTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/appsettings.json ProjectCeres/Program.cs ProjectCeres/Controllers/Api/HealthApiController.cs ProjectCeres.Tests/Integration/Security/ForwardedHeadersTests.cs
git commit -m "feat(14): forwarded-headers middleware (fails closed) + spoof-rejection test"
```

---

## Task 7: CORS wiring (inert) + CSP-hash drift guard test

**Files:**
- Modify: `ProjectCeres/appsettings.json` (`Cors` section)
- Modify: `ProjectCeres/Program.cs` (register CORS services + `UseCors` after `UseRouting`)
- Test: add a drift-guard test to `SecurityHeadersTests`

**Interfaces:**
- Produces: `Cors` config section (empty origins today).

- [ ] **Step 1: Write the CSP-hash drift-guard test** (append to `SecurityHeadersTests`)

```csharp
    [Fact]
    public async Task Csp_script_hashes_match_the_served_shell()
    {
        var shell = await _client.GetStringAsync("/");
        var res = await _client.GetAsync("/api/health");
        var csp = string.Join(" ", res.Headers.GetValues("Content-Security-Policy"));
        // Every inline <script> in the served shell must have its hash present in script-src.
        var re = new System.Text.RegularExpressions.Regex(@"<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)</script>");
        foreach (System.Text.RegularExpressions.Match m in re.Matches(shell))
        {
            var body = m.Groups[1].Value;
            var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(body)));
            csp.Should().Contain(hash, "every inline shell script must be blessed by its CSP hash");
        }
    }
```

- [ ] **Step 2: Run to verify it passes already** (hashes computed in Task 2/3)

Run: `~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SecurityHeadersTests.Csp_script_hashes_match_the_served_shell"`
Expected: PASS. (If FAIL, the build-time hash wiring from Task 2 is off — fix there, do not weaken this test.)

- [ ] **Step 3: Add the Cors config section**

In `ProjectCeres/appsettings.json`:
```json
"Cors": {
  "AllowedOrigins": []
}
```

- [ ] **Step 4: Register CORS**

In service registration (near the other `builder.Services.Add...` calls):
```csharp
// CORS (Stage 14.6): inert today — same-origin deployment, empty AllowedOrigins.
// FIXME(Stage 16): populate Cors:AllowedOrigins IFF a separate SPA origin is introduced.
builder.Services.AddCors(options => options.AddDefaultPolicy(p =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) p.WithOrigins(origins).AllowCredentials().AllowAnyHeader().AllowAnyMethod();
}));
```
After `app.UseRouting();` (line ~1135), before `app.UseAuthentication();`:
```csharp
app.UseCors();
```

- [ ] **Step 5: Verify the full security test file passes + build**

Run:
```bash
~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~Security"
~/.dotnet/dotnet build ProjectCeres.sln -c Debug
```
Expected: all pass; `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/appsettings.json ProjectCeres/Program.cs ProjectCeres.Tests/Integration/Security/SecurityHeadersTests.cs
git commit -m "feat(14): CORS wiring (inert, same-origin) + CSP-hash drift guard"
```

---

## Task 8: react/no-danger documented exception for the chart primitive

**Files:**
- Modify: `ProjectCeres.Client/src/components/ui/chart.tsx`
- Test: `pnpm --dir ProjectCeres.Client lint` passes; `pnpm build` passes.

**Interfaces:** none.

- [ ] **Step 1: Confirm the current lint state**

Run: `pnpm --dir ProjectCeres.Client lint`
Expected: either passes (rule not currently firing) or flags `react/no-danger` at `chart.tsx`. Note which.

- [ ] **Step 2: Add the documented exception**

At the `dangerouslySetInnerHTML` site in `chart.tsx` (~line 92), add a line-scoped disable WITH the false-positive rationale (per the spec — this is a verified false positive, static CSS from a typed config, no user input):
```tsx
// react/no-danger: false positive — injected content is static CSS generated
// from the typed ChartConfig (no user/network input). CSP style-src covers it.
// See docs/security-model.md § react/no-danger exception (Stage 14).
// eslint-disable-next-line react/no-danger
```
(Only add this if Step 1 showed the rule firing. If it does not fire today, instead ensure the exception is recorded in docs at stage close — Task 9 — and skip the inline directive.)

- [ ] **Step 3: Verify lint + build pass**

Run: `pnpm --dir ProjectCeres.Client lint && pnpm --dir ProjectCeres.Client build`
Expected: both pass.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/components/ui/chart.tsx
git commit -m "chore(14): document react/no-danger false-positive on chart primitive"
```

---

## Task 9: Stage close — docs sync + verification checklist

**Files:**
- Modify: `docs/security-model.md` (CSP nonce→hash correction; react/no-danger exception entry)
- Modify: `docs/roadmap-phase-three.md` (tick Stage 14 checklist items now covered)

- [ ] **Step 1: Run sync-docs** against the Stage 14 diff (CSP skeleton correction + the documented exception). Follow the skill.

- [ ] **Step 2: Tick the Stage 14 roadmap checklist** items now covered by automated tests; leave browser-only items unchecked; confirm the three Stage 16 deferral `[ ]` lines remain.

- [ ] **Step 3: Full verification (Definition of Done)**

Run:
```bash
pnpm --dir ProjectCeres.Client build
pnpm --dir ProjectCeres.Client test -- --run
~/.dotnet/dotnet build ProjectCeres.sln -c Debug
~/.dotnet/dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~Security"
```
Expected: all exit 0. (The Stop hook runs the full suite; do not run it concurrently.)

- [ ] **Step 4: Manual browser verification** (cannot be automated): load the app, confirm no CSP violations in the browser console on the dashboard (charts render), login/logout, and a page with a chart. If browser access is unavailable, hand this checklist to the user with the specific URLs.

- [ ] **Step 5: Commit the doc sync**

```bash
git add docs/security-model.md docs/roadmap-phase-three.md
git commit -m "docs(14): CSP nonce→hash correction, react/no-danger exception, roadmap close-out"
```

---

## Self-Review

**Spec coverage:** 14.1 CSP → Task 2+3+7; 14.2–14.4 + COOP/CORP/Permissions → Task 3; 14.8 cache + Clear-Site-Data → Task 5; 14.7 forwarded-headers → Task 6; 14.6 CORS → Task 7; /csp-report → Task 4; build-time hashes → Task 2; react/no-danger reconciliation → Task 8+9; doc correction → Task 9; Stage 16 deferrals → already in roadmap (committed with the spec). All covered.

**Placeholder scan:** The `FIXME(Stage 16)` markers are intentional tripwires, not plan gaps. The notes to "confirm exact NetEscapades method names against the installed version" and "match the existing fixture base-class" are deliberate — the shape is pinned, the package/fixture names are environment facts the executor reads, not invented placeholders.

**Type consistency:** `AuthRateLimitPolicies.CspReportByIp` defined in Task 4, consumed in Task 4's controller. `csp-hashes.json` produced in Task 2, consumed in Task 3's `LoadScriptHashes`. `SecurityHeadersConfig.Build` signature consistent between Task 3 definition and Program.cs call. `CacheHeaderMiddleware` consistent Task 5. No signature drift.
