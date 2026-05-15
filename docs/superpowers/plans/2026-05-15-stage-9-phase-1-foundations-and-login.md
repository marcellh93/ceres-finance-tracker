# Stage 9 Phase 1 — Foundations + Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the SPA foundations (i18n, AuthLayout, API client, auth context, form library, OTP/QR libs) and the `/login` page + globe language toggle as a working end-to-end vertical slice the user can validate in a real browser.

**Architecture:** Phase 1 covers commits 1–6 from the spec at `docs/superpowers/specs/2026-05-15-stage-9-auth-spa-pages-design.md` § 7 Phase 1. The SPA splits its router tree into two top-level branches — public (`<AuthLayout>`, no app shell) and protected (`<RequireAuth><AppLayout>`). Server-side, one new endpoint lands (`GET /api/auth/me`) so the SPA's auth context has a session-state source. The phase ends at a deliberate UX-review pause — the user opens `/app/login` in a real browser and validates the design + foundation choices before Phase 2 starts. **Phases 2, 3, 4 will be planned in separate files after this one ships and the UX review passes.**

**Tech Stack:**
- Server: .NET 10 ASP.NET Core MVC, EF Core + Postgres, xUnit + FluentAssertions + Moq
- Client: React 19 + Vite + TypeScript, react-router-dom v7, react-i18next, react-hook-form + zod, shadcn/ui (`base-nova`) on top of `@base-ui/react`, vitest + RTL + vitest-axe
- Package manager: **pnpm only** (never npm — see `feedback_pnpm_only_never_npm` and project memory). Use `pnpm --dir ProjectCeres.Client` from the repo root, never `cd && pnpm`.

**Standing rules to honour throughout:**
- Stay on `main`. No branches, no worktrees, no Co-Authored-By trailer in commits.
- TDD per `docs/testing.md` § Rules: tests first on every commit; never weaken/skip a failing test to make it pass.
- For frontend commits: invoke the `frontend-design` skill before visual decisions; run the UX/UI verification checklist (browser, 375px mobile, golden + edge); run `web-design-guidelines` audit pre-commit.
- For integration tests against the shared `IntegrationTests` xUnit collection: filter every DB query by a per-test marker (per-test email suffix is the established pattern; see `feedback_filter_test_queries_by_test_data`).
- Run `pnpm` commands and `dotnet test` in the **foreground**, not background; the auth subset takes ~5 minutes by design.
- Doc-sync runs in the SAME commit as the code change (per `feedback_sync_docs_before_spa_commits`); `sync-docs` skill runs against the diff before each commit B1+ closes.

**Spec section the phase finishes (after commit 6):**
- ✅ Stage 9.1 (`/login` page) — verification checklist items 967–976
- ✅ Stage 9.9 partial (globe language toggle) — items 1043–1047
- ✅ Locked design call captured in roadmap (line 977 rewrite)
- ✅ Foundation pieces ready for Phases 2–4

---

## File Structure

This phase creates and modifies these files. Each task below names the exact paths.

### Server (.NET)

**Create:**
- `ProjectCeres/Common/Localization/LanguagePreferenceMiddleware.cs` — reads `lang` cookie, sets `CultureInfo.CurrentUICulture` for the request
- `ProjectCeres/ViewModels/Auth/MeResponse.cs` — DTO returned by `GET /api/auth/me`
- `ProjectCeres.Tests/Integration/Authentication/MeEndpointTests.cs` — xUnit tests for `GET /api/auth/me`

**Modify:**
- `ProjectCeres/Controllers/Api/AuthController.cs` — add `Me()` action returning `MeResponse`
- `ProjectCeres/Program.cs` — register `LanguagePreferenceMiddleware` before `UseRouting`

### Client (React)

**Create:**
- `ProjectCeres.Client/src/app/i18n/i18n.ts` — `react-i18next` config + `lang` cookie helpers
- `ProjectCeres.Client/src/app/i18n/locales/en.json` — English translations (only the keys this phase needs)
- `ProjectCeres.Client/src/app/i18n/locales/es.json` — Spanish translations (parity with `en.json`)
- `ProjectCeres.Client/src/app/i18n/i18n.test.ts` — parity test: every key in `en.json` exists in `es.json`
- `ProjectCeres.Client/src/app/layout/AuthLayout.tsx` — centered card, BrandMark, Outlet, footer slot
- `ProjectCeres.Client/src/app/layout/AuthLayout.a11y.test.tsx` — axe pass on the empty layout
- `ProjectCeres.Client/src/app/auth/RequireAuth.tsx` — guard component; redirects anon visitors to `/login?redirect=...`
- `ProjectCeres.Client/src/app/auth/RequireAuth.test.tsx` — guard behaviour tests
- `ProjectCeres.Client/src/app/lib/api-client.ts` — `apiFetch()` wrapper + CSRF handshake + error mapping
- `ProjectCeres.Client/src/app/lib/api-client.test.ts` — wrapper behaviour tests
- `ProjectCeres.Client/src/app/auth/csrf.ts` — `__Host-XSRF` cookie reader
- `ProjectCeres.Client/src/app/auth/auth-context.tsx` — `<AuthProvider>` + `useAuth()`
- `ProjectCeres.Client/src/app/auth/auth-context.test.tsx` — context behaviour tests
- `ProjectCeres.Client/src/app/auth/use-step-up.ts` — `useStepUp()` hook (returned dialog wired in Phase 4)
- `ProjectCeres.Client/src/app/auth/use-step-up.test.tsx` — hook behaviour tests
- `ProjectCeres.Client/src/app/auth/schemas/login.schema.ts` — zod schema for the login form
- `ProjectCeres.Client/src/app/pages/auth/Login.tsx` — `/login` page
- `ProjectCeres.Client/src/app/pages/auth/Login.test.tsx` — login behaviour tests
- `ProjectCeres.Client/src/app/pages/auth/Login.a11y.test.tsx` — axe pass on `/login`
- `ProjectCeres.Client/src/app/components/auth/LanguageToggle.tsx` — globe-icon dropdown
- `ProjectCeres.Client/src/app/components/auth/LanguageToggle.test.tsx` — toggle behaviour tests

**Modify:**
- `ProjectCeres.Client/src/app/main.tsx` — wrap `<App />` with `<I18nextProvider>` + `<AuthProvider>`
- `ProjectCeres.Client/src/app/App.tsx` — split route tree into public and protected branches
- `ProjectCeres.Client/src/app/App.test.tsx` — extend route table to assert the public branch routes
- `ProjectCeres.Client/package.json` — add `react-i18next`, `i18next`, `i18next-browser-languagedetector`, `react-hook-form`, `zod`, `@hookform/resolvers`, `qrcode.react`; add shadcn `input-otp`

### Docs

**Create:**
- `docs/decisions/ADR-0074-react-hook-form-with-zod-for-spa-forms.md` — ADR for the form-library choice

**Modify (per-commit doc-sync):**
- `docs/roadmap-phase-three.md` — line 977 rewrite (commit 6); tick checklist items 967–976 + 1043–1047 (commit 6)
- `docs/planning-phase3.md` — § 14 implementation order updated (commit 5)
- `docs/planning-phase3-spa-migration.md` — § 2 (route map) gains the seven new public routes (commit 3); § 8 (Razor view retirement) updated (commits 2 + 3)

---

## Task 1: Add `GET /api/auth/me` server endpoint

**Files:**
- Create: `ProjectCeres/ViewModels/Auth/MeResponse.cs`
- Create: `ProjectCeres.Tests/Integration/Authentication/MeEndpointTests.cs`
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs` — add `Me()` action

**Background:** The SPA's auth context calls this once on mount to decide if the user is logged in and to render auth-aware UI (TOTP-enabled badges, the backup-code banner). Per the spec § 1, it returns `{ userId, email, twoFactorEnabled, lastReauthAt, backupCodesRemaining, usedBackupCodeAtLastLogin }`. `Cache-Control: no-store`. Returns 401 for anonymous requests.

**Why a DTO instead of returning the `ApplicationUser`:** never serialise an Identity entity directly — it includes the password hash, security stamp, lockout state, and concurrency stamp. The DTO is the contract; the entity is the storage.

- [ ] **Step 1: Write the failing test file**

```csharp
// ProjectCeres.Tests/Integration/Authentication/MeEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class MeEndpointTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public MeEndpointTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@me-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Me_returns_401_when_anonymous()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_user_shape_when_authenticated()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "shape@me-test.local");
        var client = await AuthTestFixture.AuthenticatedClientAsync(_factory, user.Id);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeResponseShape>();
        body!.UserId.Should().Be(user.Id);
        body.Email.Should().Be("shape@me-test.local");
        body.TwoFactorEnabled.Should().BeFalse();
        body.BackupCodesRemaining.Should().Be(0);
        body.UsedBackupCodeAtLastLogin.Should().BeFalse();
    }

    [Fact]
    public async Task Me_sets_no_store_cache_control()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "cache@me-test.local");
        var client = await AuthTestFixture.AuthenticatedClientAsync(_factory, user.Id);

        var response = await client.GetAsync("/api/auth/me");

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private sealed record MeResponseShape(
        Guid UserId,
        string Email,
        bool TwoFactorEnabled,
        long? LastReauthAt,
        int BackupCodesRemaining,
        bool UsedBackupCodeAtLastLogin);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run from repo root:
```bash
dotnet test --filter "FullyQualifiedName~MeEndpointTests" --logger "console;verbosity=normal"
```

Expected: 3 failures (404 not found from missing endpoint, plus build failure on missing `MeResponse` if the DTO isn't created yet — that's fine, the next steps fix it).

- [ ] **Step 3: Create the response DTO**

Create `ProjectCeres/ViewModels/Auth/MeResponse.cs`:

```csharp
namespace ProjectCeres.ViewModels.Auth;

/// <summary>
/// Returned by GET /api/auth/me. Used by the SPA's auth context to decide
/// session state and render auth-aware UI. Never serialise ApplicationUser
/// directly — it carries the password hash and security stamp.
/// </summary>
/// <param name="UserId">The user's primary key.</param>
/// <param name="Email">Email address (the canonical user identifier).</param>
/// <param name="TwoFactorEnabled">True if the user has TOTP enrolled.</param>
/// <param name="LastReauthAt">Unix-seconds timestamp of last reauthentication, or null if not yet reauthenticated this session.</param>
/// <param name="BackupCodesRemaining">Count of unused backup codes the user holds (0 if MFA is off).</param>
/// <param name="UsedBackupCodeAtLastLogin">True if the user's most recent successful TOTP step used a backup code rather than the authenticator app — drives the dashboard banner in Phase 4.</param>
public sealed record MeResponse(
    Guid UserId,
    string Email,
    bool TwoFactorEnabled,
    long? LastReauthAt,
    int BackupCodesRemaining,
    bool UsedBackupCodeAtLastLogin);
```

- [ ] **Step 4: Add the `Me()` action to `AuthController`**

Open `ProjectCeres/Controllers/Api/AuthController.cs`. Add this method anywhere among the other actions (logical placement: right after the `Csrf` action at line 408). Also add the namespace import at the top of the file: `using ProjectCeres.ViewModels.Auth;`.

```csharp
    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return UnauthorizedEnvelope("UNAUTHENTICATED", "Authentication required.");

        var lastReauthClaim = User.FindFirst(SessionConstants.LastReauthAtClaim)?.Value;
        long? lastReauthAt = long.TryParse(lastReauthClaim, out var v) ? v : null;

        var backupCodesRemaining = user.TwoFactorEnabled
            ? await _db.MfaBackupCodes.CountAsync(c => c.UserId == user.Id && c.UsedAt == null, HttpContext.RequestAborted)
            : 0;

        // Phase 4 wires UsedBackupCodeAtLastLogin properly (it depends on a new column
        // on UserSession written by /login/totp on the backup-code path). For Phase 1
        // it's always false — the banner that consumes it doesn't ship until Phase 4.
        var usedBackupCodeAtLastLogin = false;

        Response.Headers.CacheControl = "no-store";

        return Ok(new MeResponse(
            UserId: user.Id,
            Email: user.Email!,
            TwoFactorEnabled: user.TwoFactorEnabled,
            LastReauthAt: lastReauthAt,
            BackupCodesRemaining: backupCodesRemaining,
            UsedBackupCodeAtLastLogin: usedBackupCodeAtLastLogin));
    }
```

**Note for the implementer:** if `_db.MfaBackupCodes` isn't the right entity name, grep for `BackupCode` in `ProjectCeres/Data/AppDbContext.cs` to find the actual `DbSet<>` name — the existing `MfaController.RegenerateBackupCodes` already queries it, so the property exists. Adjust the `CountAsync` call to match.

- [ ] **Step 5: Add `AuthenticatedClientAsync` helper to `AuthTestFixture` if missing**

Check `ProjectCeres.Tests/Integration/Authentication/AuthTestFixture.cs` for an existing helper called `AuthenticatedClientAsync` (or similar — `AuthenticatedClient`, `LoggedInClient`, etc.). Other tests in this folder authenticate by minting cookies via the existing fixture; if a one-call helper already exists, use it. If not, the test already does authentication via the existing fixture pattern — adapt the test setup to match the project's prevailing pattern (look at `LogoutEndpointTests.cs:36–60` for a working example of "register + login" inside a single test).

The point: **do not invent a new authentication helper if the project has one.** Find it, use it.

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~MeEndpointTests" --logger "console;verbosity=normal"
```

Expected: 3 passes. If the Cache-Control test fails because the header is rendered as `no-cache, no-store` instead of pure `no-store`, that's still acceptable — the assertion is `NoStore.Should().BeTrue()` which passes for the combined header. If it fails for any other reason, fix the response code (don't weaken the test).

- [ ] **Step 7: Run the full auth subset to confirm no regressions**

```bash
dotnet test --filter "FullyQualifiedName~Authentication" --logger "console;verbosity=normal"
```

Expected: green. Takes ~5 minutes; do not abort silent runs (Argon2id cost is by design).

- [ ] **Step 8: Commit**

```bash
git -C <repo> add \
  ProjectCeres/ViewModels/Auth/MeResponse.cs \
  ProjectCeres/Controllers/Api/AuthController.cs \
  ProjectCeres.Tests/Integration/Authentication/MeEndpointTests.cs

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9): add GET /api/auth/me endpoint

Stage 9 prerequisite — the SPA auth context calls this once on mount
to decide session state and render auth-aware UI. Returns:
  { userId, email, twoFactorEnabled, lastReauthAt, backupCodesRemaining,
    usedBackupCodeAtLastLogin }

UsedBackupCodeAtLastLogin is wired to a placeholder false here; Phase 4
will populate it from a new UserSession column written by /login/totp on
the backup-code path. The banner that consumes it doesn't ship until
Phase 4 either, so the placeholder is intentionally inert.

Cache-Control: no-store. 401 with the project envelope when anonymous.
DTO sits in ViewModels/Auth/ alongside the existing register/login DTOs;
never serialises ApplicationUser directly.
EOF
)"
```

---

## Task 2: Add `react-i18next` + `lang` cookie middleware (commit 2)

**Files:**
- Create: `ProjectCeres/Common/Localization/LanguagePreferenceMiddleware.cs`
- Create: `ProjectCeres.Client/src/app/i18n/i18n.ts`
- Create: `ProjectCeres.Client/src/app/i18n/locales/en.json`
- Create: `ProjectCeres.Client/src/app/i18n/locales/es.json`
- Create: `ProjectCeres.Client/src/app/i18n/i18n.test.ts`
- Modify: `ProjectCeres/Program.cs` — register the middleware before `UseRouting`
- Modify: `ProjectCeres.Client/src/app/main.tsx` — initialise i18n before `<App />`
- Modify: `ProjectCeres.Client/package.json` — add deps via pnpm

**Background:** Spec § 1, foundation piece A1. The `lang` cookie is `SameSite=Lax`, `Secure`, **NOT** HttpOnly (the SPA needs to read it to bootstrap the language), 1-year expiry. Server-side middleware reads the same cookie and sets `CultureInfo.CurrentUICulture` so any remaining Razor pages also honour the language. After login, the user's account language preference takes over on the next page load (that wiring lands in a later phase).

**Per-commit doc-sync:** `planning-phase3-spa-migration.md` § 8 (Razor view retirement timing for the bilingual middleware) updates in this commit.

- [ ] **Step 1: Add the i18n npm packages via pnpm**

```bash
pnpm --dir ProjectCeres.Client add react-i18next i18next i18next-browser-languagedetector
```

Confirm `package.json` shows the three new entries under `dependencies`.

- [ ] **Step 2: Write the parity test (failing)**

Create `ProjectCeres.Client/src/app/i18n/i18n.test.ts`:

```typescript
import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import es from './locales/es.json';

function flattenKeys(obj: Record<string, unknown>, prefix = ''): string[] {
  const keys: string[] = [];
  for (const [k, v] of Object.entries(obj)) {
    const path = prefix ? `${prefix}.${k}` : k;
    if (v !== null && typeof v === 'object') {
      keys.push(...flattenKeys(v as Record<string, unknown>, path));
    } else {
      keys.push(path);
    }
  }
  return keys;
}

describe('i18n locale parity', () => {
  it('every key in en.json exists in es.json', () => {
    const enKeys = new Set(flattenKeys(en));
    const esKeys = new Set(flattenKeys(es));
    const missing = [...enKeys].filter((k) => !esKeys.has(k));
    expect(missing, `missing in es.json: ${missing.join(', ')}`).toHaveLength(0);
  });

  it('every key in es.json exists in en.json', () => {
    const enKeys = new Set(flattenKeys(en));
    const esKeys = new Set(flattenKeys(es));
    const extra = [...esKeys].filter((k) => !enKeys.has(k));
    expect(extra, `unexpected in es.json: ${extra.join(', ')}`).toHaveLength(0);
  });

  it('no value is an empty string', () => {
    const allValues = (obj: Record<string, unknown>): unknown[] =>
      Object.values(obj).flatMap((v) =>
        v !== null && typeof v === 'object' ? allValues(v as Record<string, unknown>) : [v]
      );
    expect(allValues(en).every((v) => typeof v === 'string' && v.length > 0)).toBe(true);
    expect(allValues(es).every((v) => typeof v === 'string' && v.length > 0)).toBe(true);
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/i18n/i18n.test.ts
```

Expected: fail with "Cannot find module './locales/en.json'".

- [ ] **Step 4: Create the locale files**

Create `ProjectCeres.Client/src/app/i18n/locales/en.json`:

```json
{
  "auth": {
    "login": {
      "title": "Sign in",
      "emailLabel": "Email",
      "passwordLabel": "Password",
      "rememberMeLabel": "Remember me on this device",
      "submit": "Sign in",
      "submitting": "Signing in…",
      "forgotPasswordLink": "Forgot password?",
      "createAccountLink": "Create an account",
      "errors": {
        "invalidCredentials": "Email or password is incorrect.",
        "accountLocked": "Account locked. Check your email for an unlock link.",
        "emailNotConfirmed": "Verify your email first. Check your inbox.",
        "resendVerification": "Resend verification email"
      }
    },
    "languageToggle": {
      "ariaLabel": "Change language",
      "english": "English",
      "spanish": "Español"
    }
  }
}
```

Create `ProjectCeres.Client/src/app/i18n/locales/es.json` (parity — every key from en.json present, translated):

```json
{
  "auth": {
    "login": {
      "title": "Iniciar sesión",
      "emailLabel": "Correo electrónico",
      "passwordLabel": "Contraseña",
      "rememberMeLabel": "Recordarme en este dispositivo",
      "submit": "Iniciar sesión",
      "submitting": "Iniciando sesión…",
      "forgotPasswordLink": "¿Olvidaste tu contraseña?",
      "createAccountLink": "Crear una cuenta",
      "errors": {
        "invalidCredentials": "El correo electrónico o la contraseña no son correctos.",
        "accountLocked": "Cuenta bloqueada. Consulta tu correo para un enlace de desbloqueo.",
        "emailNotConfirmed": "Primero verifica tu correo. Consulta tu bandeja de entrada.",
        "resendVerification": "Reenviar correo de verificación"
      }
    },
    "languageToggle": {
      "ariaLabel": "Cambiar idioma",
      "english": "English",
      "spanish": "Español"
    }
  }
}
```

- [ ] **Step 5: Re-run the parity test to verify it passes**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/i18n/i18n.test.ts
```

Expected: 3 passes.

- [ ] **Step 6: Create the i18n config**

Create `ProjectCeres.Client/src/app/i18n/i18n.ts`:

```typescript
import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import en from './locales/en.json';
import es from './locales/es.json';

export const SUPPORTED_LANGUAGES = ['en', 'es'] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

export const LANG_COOKIE_NAME = 'lang';

/**
 * Read the lang cookie. Returns null if absent or not in SUPPORTED_LANGUAGES.
 * Exposed for use by the language toggle component.
 */
export function readLangCookie(): SupportedLanguage | null {
  if (typeof document === 'undefined') return null;
  const match = document.cookie.match(/(?:^|;\s*)lang=([^;]+)/);
  if (!match) return null;
  const value = decodeURIComponent(match[1]);
  return (SUPPORTED_LANGUAGES as readonly string[]).includes(value)
    ? (value as SupportedLanguage)
    : null;
}

/**
 * Write the lang cookie. SameSite=Lax, Secure, NOT HttpOnly (the SPA reads it),
 * 1-year expiry. Called by the language toggle when the user switches.
 */
export function writeLangCookie(lang: SupportedLanguage): void {
  if (typeof document === 'undefined') return;
  const oneYearSeconds = 60 * 60 * 24 * 365;
  document.cookie = `${LANG_COOKIE_NAME}=${encodeURIComponent(lang)}; Path=/; Max-Age=${oneYearSeconds}; SameSite=Lax; Secure`;
}

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: { en: { translation: en }, es: { translation: es } },
    fallbackLng: 'en',
    supportedLngs: [...SUPPORTED_LANGUAGES],
    detection: {
      order: ['cookie', 'navigator'],
      lookupCookie: LANG_COOKIE_NAME,
      caches: ['cookie'],
      cookieMinutes: 60 * 24 * 365, // 1 year
      cookieOptions: { path: '/', sameSite: 'lax', secure: true },
    },
    interpolation: { escapeValue: false }, // React already escapes
  });

export default i18n;
```

- [ ] **Step 7: Wire i18n into `main.tsx`**

Open `ProjectCeres.Client/src/app/main.tsx`. Add the import for the i18n config (which has top-level side effects that initialise it). Place the import alongside the other imports near the top:

```typescript
import './i18n/i18n';
```

The file's other contents stay unchanged. The side-effect import is enough — `react-i18next`'s `useTranslation()` hook will read from the module-level `i18n` instance once it's been initialised.

- [ ] **Step 8: Create the server-side `LanguagePreferenceMiddleware`**

Create `ProjectCeres/Common/Localization/LanguagePreferenceMiddleware.cs`:

```csharp
using System.Globalization;

namespace ProjectCeres.Common.Localization;

/// <summary>
/// Reads the `lang` cookie set by the SPA's language toggle and applies it
/// to the request's UI culture so any Razor views (the legacy auth pages
/// still under Views/Account/* until Stage 11.8 cleanup deletes them)
/// render in the same language the SPA is showing.
///
/// Cookie attributes (set by the SPA, mirrored here for read-only consumption):
///   SameSite=Lax, Secure, NOT HttpOnly (the SPA needs to read it), 1-year expiry.
/// </summary>
public sealed class LanguagePreferenceMiddleware
{
    private static readonly string[] SupportedLangs = ["en", "es"];
    private const string CookieName = "lang";

    private readonly RequestDelegate _next;

    public LanguagePreferenceMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out var lang) &&
            !string.IsNullOrEmpty(lang) &&
            SupportedLangs.Contains(lang, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(lang);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
                // Unknown language code in cookie — ignore and fall through.
            }
        }

        await _next(context);
    }
}
```

- [ ] **Step 9: Register the middleware in `Program.cs`**

Open `ProjectCeres/Program.cs`. Find the line `app.UseRouting();`. Insert this BEFORE it (so the culture is set before route handlers run):

```csharp
app.UseMiddleware<ProjectCeres.Common.Localization.LanguagePreferenceMiddleware>();
```

Add the namespace import at the top of the file if it isn't already pulled in via the fully-qualified name above.

- [ ] **Step 10: Run client tests to verify nothing regressed**

```bash
pnpm --dir ProjectCeres.Client test --run
```

Expected: green (existing tests + the new i18n parity test). The i18n side-effect import in `main.tsx` doesn't affect existing tests because they render via `MemoryRouter` without going through `main.tsx`.

- [ ] **Step 11: Run server build to verify the middleware compiles**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj
```

Expected: 0 errors, 0 new warnings.

- [ ] **Step 12: Doc-sync — update `planning-phase3-spa-migration.md` § 8**

Add a one-line entry under § 8 (Razor view retirement) noting that `LanguagePreferenceMiddleware` ships in Stage 9 and serves the remaining `Views/Account/*` Razor pages until Stage 11.8 deletes them. Use the `sync-docs` skill to find the right location and phrasing.

- [ ] **Step 13: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Client/package.json \
  ProjectCeres.Client/pnpm-lock.yaml \
  ProjectCeres.Client/src/app/i18n/ \
  ProjectCeres.Client/src/app/main.tsx \
  ProjectCeres/Common/Localization/LanguagePreferenceMiddleware.cs \
  ProjectCeres/Program.cs \
  docs/planning-phase3-spa-migration.md

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9): add react-i18next + lang cookie + Razor middleware

Stage 9 foundation A1. SPA-side: react-i18next + i18next +
i18next-browser-languagedetector (pnpm — never npm). Cookie name `lang`,
SameSite=Lax, Secure, NOT HttpOnly (the SPA reads it), 1-year expiry.

Locale files start with only the keys this phase needs (the /login page
+ language toggle); subsequent phases add per-page namespaces. Parity
test asserts en.json ↔ es.json key-set parity at test time so a missing
translation never reaches a user's screen.

Server-side: LanguagePreferenceMiddleware reads the same cookie and
sets CurrentUICulture so the remaining Views/Account/* Razor pages
honour the SPA's language until Stage 11.8 deletes them.
EOF
)"
```

---

## Task 3: Create `<AuthLayout>` + `<RequireAuth>` + split `App.tsx` routes (commit 3)

**Files:**
- Create: `ProjectCeres.Client/src/app/layout/AuthLayout.tsx`
- Create: `ProjectCeres.Client/src/app/layout/AuthLayout.a11y.test.tsx`
- Create: `ProjectCeres.Client/src/app/auth/RequireAuth.tsx`
- Create: `ProjectCeres.Client/src/app/auth/RequireAuth.test.tsx`
- Modify: `ProjectCeres.Client/src/app/App.tsx` — split route tree into public + protected branches
- Modify: `ProjectCeres.Client/src/app/App.test.tsx` — extend route table to assert the public branch routes

**Background:** Spec § 1 foundation piece A2 + § 3 architecture. The router tree splits into two top-level branches: a public branch wrapping `<AuthLayout>` (centered card, no sidebar), and a protected branch wrapping `<RequireAuth><AppLayout>` around everything that exists today. `<RequireAuth>` is a small guard component — for this commit it's stubbed to always allow through (the real auth-state check arrives in Task 4 with the auth context). That keeps Task 3 focused on the layout + routing shape; Task 4 wires the actual gate.

**Design call from the brainstorm:** "A — Plain canvas." Centered card on a soft neutral background. Brand wordmark inside the card. Closest to shadcn defaults; lowest visual risk. The user explicitly said "for a beta phase, it works."

**Per-commit doc-sync:** `planning-phase3-spa-migration.md` § 2 (route map) gains the seven new public routes.

- [ ] **Step 1: Write the AuthLayout a11y test (failing)**

Create `ProjectCeres.Client/src/app/layout/AuthLayout.a11y.test.tsx`:

```typescript
import { render } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, it } from 'vitest';
import { AuthLayout } from './AuthLayout';
import { expectNoA11yViolations } from '../lib/test-axe';

describe('AuthLayout a11y', () => {
  it('renders without serious or critical axe violations', async () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/login']}>
        <Routes>
          <Route element={<AuthLayout />}>
            <Route path="login" element={<main><h1>Test page</h1></main>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    await expectNoA11yViolations(container);
  });
});
```

- [ ] **Step 2: Write the RequireAuth test (failing)**

Create `ProjectCeres.Client/src/app/auth/RequireAuth.test.tsx`:

```typescript
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { RequireAuth } from './RequireAuth';

describe('RequireAuth', () => {
  it('renders children when allowed (Phase 1 stub: always allow)', () => {
    render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route
            element={<RequireAuth><div>protected</div></RequireAuth>}
          >
            <Route path="dashboard" element={<div>protected</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByText('protected')).toBeDefined();
  });

  // Note: the "redirect to /login when anon" assertion lives in Task 4's
  // auth-context tests, since RequireAuth gains its real gate then.
});
```

- [ ] **Step 3: Run the failing tests**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/layout/AuthLayout.a11y.test.tsx src/app/auth/RequireAuth.test.tsx
```

Expected: both fail (modules don't exist).

- [ ] **Step 4: Create `AuthLayout.tsx`**

Create `ProjectCeres.Client/src/app/layout/AuthLayout.tsx`:

```typescript
import { Outlet } from 'react-router-dom';
import { Card } from '@/components/ui/card';
import { BrandMark } from './BrandMark';

/**
 * Layout for unauthenticated auth pages: centered card on a soft
 * neutral background, no sidebar, no top bar. Brand wordmark inside
 * the card; the language toggle slot in the footer is filled by
 * <LanguageToggle /> which is mounted in the Outlet's siblings —
 * see Task 6 for the full mounting.
 *
 * Responsive: identical at mobile / tablet / desktop per
 * docs/planning-phase3-responsive.md (single-column centered card on
 * every tier).
 */
export function AuthLayout() {
  return (
    <div className="min-h-dvh w-full bg-muted/30 flex items-center justify-center p-4 sm:p-6">
      <Card className="w-full max-w-[420px] p-6 sm:p-8 space-y-6">
        <header className="flex flex-col items-center gap-2">
          <BrandMark />
        </header>
        <main>
          <Outlet />
        </main>
        {/* Footer slot for <LanguageToggle /> — filled in Task 6.
            Empty <footer> kept here so axe sees a landmark. */}
        <footer className="flex justify-center" data-slot="auth-footer" />
      </Card>
    </div>
  );
}
```

- [ ] **Step 5: Create the stub `RequireAuth.tsx`**

Create `ProjectCeres.Client/src/app/auth/RequireAuth.tsx`:

```typescript
import type { ReactNode } from 'react';

/**
 * Phase 1 stub: always allows the children through. The real auth-state
 * gate arrives in Task 4 once <AuthProvider> exists. Splitting the layout
 * commit (this one) from the auth-context commit (Task 4) keeps each
 * commit reviewable and testable in isolation.
 *
 * Real behaviour after Task 4: reads useAuth(); if status === 'anon',
 * navigates to /login?redirect=<currentPath>; otherwise renders children.
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  return <>{children}</>;
}
```

- [ ] **Step 6: Modify `App.tsx` to split route tree**

Open `ProjectCeres.Client/src/app/App.tsx`. Replace the entire `App` component body. Key change: wrap the existing `<AppLayout>` route in a `<RequireAuth>`, and add a new `<AuthLayout>` branch above it.

The new structure:

```typescript
import { Navigate, Route, Routes } from 'react-router-dom';
import { AppLayout } from './layout/AppLayout';
import { AuthLayout } from './layout/AuthLayout';
import { RequireAuth } from './auth/RequireAuth';
import { Accounts } from './pages/Accounts';
// ... all existing imports unchanged ...

function RecurringCreateBridge() {
  const ctx = useRecurringLayoutCtx();
  return <RecurringCreate ctx={ctx} />;
}

function RecurringEditBridge() {
  const ctx = useRecurringLayoutCtx();
  return <RecurringEdit ctx={ctx} />;
}

export function App() {
  return (
    <Routes>
      {/* Public branch — auth pages with the centered-card layout, no app shell. */}
      <Route element={<AuthLayout />}>
        {/* Login lands in Task 6 of this phase; the route is registered
            here so the routing split lands first as its own commit. */}
      </Route>

      {/* Protected branch — everything that exists today, gated by RequireAuth. */}
      <Route
        element={
          <RequireAuth>
            <AppLayout />
          </RequireAuth>
        }
      >
        <Route index element={<Dashboard />} />
        <Route path="movements" element={<MovementsLayout />}>
          <Route path="new" element={<MovementCreate />} />
          <Route path=":id/edit" element={<MovementEdit />} />
        </Route>
        {/* ... all other existing routes unchanged ... */}
      </Route>
    </Routes>
  );
}
```

**Implementer note:** preserve every existing route inside the protected branch; do not change paths, components, or nesting. The only change is wrapping `<AppLayout />` with `<RequireAuth>`.

- [ ] **Step 7: Update `App.test.tsx` to cover the new branches**

Open `ProjectCeres.Client/src/app/App.test.tsx`. The existing route table should still pass — every protected route still works because `RequireAuth` is a passthrough in Phase 1. Add a new test confirming the split is structural (the public branch exists even with no children yet — landing at an unknown path inside it should hit the `NotFound` route inside the protected branch as before, since the unknown URL doesn't match any public route either).

```typescript
// Add to App.test.tsx after the existing 'App routes' describe block:

describe('App routing structure', () => {
  it('still routes existing protected pages through RequireAuth', () => {
    render(
      <MemoryRouter initialEntries={['/']}>
        <App />
      </MemoryRouter>,
    );
    // RequireAuth is a passthrough in Phase 1 — Dashboard renders.
    expect(screen.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeDefined();
  });
});
```

- [ ] **Step 8: Run all client tests**

```bash
pnpm --dir ProjectCeres.Client test --run
```

Expected: green. Every existing route still renders, and the two new test files pass. If a test fails because `RequireAuth` somehow blocks a route, that's a bug — fix `RequireAuth` (passthrough only) rather than weakening the test.

- [ ] **Step 9: Doc-sync — update `planning-phase3-spa-migration.md` § 2**

Add the seven new public routes to the route map: `/login`, `/login/totp`, `/register`, `/email-verify`, `/password-reset`, `/password-reset/confirm`, `/account/unlock`. Note in the entry that they are scaffolded in Stage 9 (this phase ships only `/login`; the other six pages land in Phases 2 and 3). Run the `sync-docs` skill to find the right section and phrasing.

- [ ] **Step 10: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Client/src/app/layout/AuthLayout.tsx \
  ProjectCeres.Client/src/app/layout/AuthLayout.a11y.test.tsx \
  ProjectCeres.Client/src/app/auth/RequireAuth.tsx \
  ProjectCeres.Client/src/app/auth/RequireAuth.test.tsx \
  ProjectCeres.Client/src/app/App.tsx \
  ProjectCeres.Client/src/app/App.test.tsx \
  docs/planning-phase3-spa-migration.md

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9): split SPA route tree into public + protected branches

Stage 9 foundation A2. The router tree gains a public top-level
branch wrapping <AuthLayout> (centered card, no app shell) alongside
the existing protected branch wrapping <AppLayout>, now gated by
<RequireAuth>.

RequireAuth is a Phase 1 passthrough — the real auth-state gate
ships in the next commit when <AuthProvider> + useAuth() land.
Splitting the routing change from the auth-state change keeps each
commit reviewable in isolation: this commit only touches structure;
nothing here can break an existing user's flow because RequireAuth
allows everything through.

AuthLayout uses the design-system Card primitive on a soft muted
background (the "plain canvas" direction the user picked in
brainstorm). Identical layout at mobile / tablet / desktop per
planning-phase3-responsive.md.
EOF
)"
```

---

## Task 4: Add API client + CSRF helper + auth context (commit 4)

**Files:**
- Create: `ProjectCeres.Client/src/app/lib/api-client.ts` — `apiFetch()` wrapper
- Create: `ProjectCeres.Client/src/app/lib/api-client.test.ts`
- Create: `ProjectCeres.Client/src/app/auth/csrf.ts` — `__Host-XSRF` cookie reader
- Create: `ProjectCeres.Client/src/app/auth/auth-context.tsx` — `<AuthProvider>` + `useAuth()`
- Create: `ProjectCeres.Client/src/app/auth/auth-context.test.tsx`
- Create: `ProjectCeres.Client/src/app/auth/use-step-up.ts` — `useStepUp()` hook
- Create: `ProjectCeres.Client/src/app/auth/use-step-up.test.tsx`
- Modify: `ProjectCeres.Client/src/app/main.tsx` — wrap `<App />` in `<AuthProvider>`
- Modify: `ProjectCeres.Client/src/app/auth/RequireAuth.tsx` — wire the real gate
- Modify: `ProjectCeres.Client/src/app/auth/RequireAuth.test.tsx` — add the redirect-when-anon test

**Background:** Spec § 1 foundation piece A3 + § 2 data flow. `apiFetch()` is a single wrapper that:
1. Runs the **CSRF handshake** for state-changing requests if not done already this session — `GET /api/auth/csrf` to set the `__Host-XSRF` cookie. Deduped via a module-level promise.
2. Reads `__Host-XSRF` and sends it as the `X-XSRF-TOKEN` header on every non-GET request.
3. Sets `credentials: 'include'` so cookies travel.
4. Maps the project's error envelope `{ error: { code, message, details? } }` into typed errors:
   - 422 with `details.length > 0` → returns `{ ok: false, fieldErrors: { fieldName: message } }` so the form can call `setError(field, ...)`
   - 422 with `details: []` → returns `{ ok: false, formError: error.message }` (for inline page-level alert)
   - 401 with code `REAUTH_REQUIRED` → throws `ReauthRequiredError`
   - 401 with other known codes → returns `{ ok: false, code, message }`
   - Network failure → throws `NetworkError`

`<AuthProvider>` calls `apiFetch('/api/auth/me')` once on mount and exposes `{ user, status, login, loginTotp, logout, refresh }`. `useStepUp()` returns a `requireStepUp(action)` function that catches `ReauthRequiredError` from the action, opens the dialog (Phase 4), and retries.

**One Phase 1 simplification:** the dialog doesn't exist yet (Phase 4). For now, `useStepUp` exposes the catch + retry plumbing but if the dialog isn't mounted, it just rethrows the error so callers know reauth is required. The signature is forward-compatible.

- [ ] **Step 1: Write the api-client tests (failing)**

Create `ProjectCeres.Client/src/app/lib/api-client.test.ts`:

```typescript
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch, ReauthRequiredError, NetworkError } from './api-client';

describe('apiFetch', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    // Reset any module-level CSRF cache between tests.
    document.cookie = '__Host-XSRF=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  });

  afterEach(() => vi.restoreAllMocks());

  function jsonResponse(body: unknown, init: ResponseInit = {}): Response {
    return new Response(JSON.stringify(body), {
      ...init,
      headers: { 'Content-Type': 'application/json', ...(init.headers ?? {}) },
    });
  }

  it('GET request includes credentials but does not run CSRF handshake', async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse({ ok: true }, { status: 200 }));

    const result = await apiFetch('/api/auth/me');

    expect(result.ok).toBe(true);
    expect(fetchSpy).toHaveBeenCalledTimes(1);
    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/auth/me',
      expect.objectContaining({ credentials: 'include', method: 'GET' }),
    );
  });

  it('POST request runs the CSRF handshake first when no cookie present', async () => {
    fetchSpy
      // Handshake call to /api/auth/csrf — sets the cookie via Set-Cookie.
      .mockImplementationOnce(async () => {
        document.cookie = '__Host-XSRF=test-csrf-token; path=/';
        return new Response(null, { status: 204 });
      })
      // The actual POST.
      .mockResolvedValueOnce(jsonResponse(null, { status: 204 }));

    await apiFetch('/api/auth/login', { method: 'POST', body: { email: 'x', password: 'y' } });

    expect(fetchSpy).toHaveBeenCalledTimes(2);
    expect(fetchSpy.mock.calls[0][0]).toBe('/api/auth/csrf');
    expect(fetchSpy.mock.calls[1][0]).toBe('/api/auth/login');
    const loginInit = fetchSpy.mock.calls[1][1] as RequestInit;
    expect(loginInit.headers).toEqual(expect.objectContaining({ 'X-XSRF-TOKEN': 'test-csrf-token' }));
  });

  it('CSRF handshake is deduped across concurrent requests', async () => {
    let handshakeCount = 0;
    fetchSpy.mockImplementation(async (url) => {
      if (url === '/api/auth/csrf') {
        handshakeCount++;
        document.cookie = '__Host-XSRF=test-csrf-token; path=/';
        return new Response(null, { status: 204 });
      }
      return jsonResponse(null, { status: 204 });
    });

    await Promise.all([
      apiFetch('/api/auth/login', { method: 'POST', body: {} }),
      apiFetch('/api/auth/login', { method: 'POST', body: {} }),
      apiFetch('/api/auth/login', { method: 'POST', body: {} }),
    ]);

    expect(handshakeCount).toBe(1);
  });

  it('maps 422 with field details into fieldErrors', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (url === '/api/auth/csrf') {
        document.cookie = '__Host-XSRF=test-csrf-token; path=/';
        return new Response(null, { status: 204 });
      }
      return jsonResponse(
        {
          error: {
            code: 'VALIDATION_ERROR',
            message: 'One or more fields are invalid.',
            details: [{ field: 'email', message: 'Required.' }],
          },
        },
        { status: 422 },
      );
    });

    const result = await apiFetch('/api/auth/login', { method: 'POST', body: {} });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.fieldErrors).toEqual({ email: 'Required.' });
    }
  });

  it('maps 422 with empty details into formError', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (url === '/api/auth/csrf') {
        document.cookie = '__Host-XSRF=test-csrf-token; path=/';
        return new Response(null, { status: 204 });
      }
      return jsonResponse(
        {
          error: { code: 'VALIDATION_ERROR', message: 'Something went wrong.', details: [] },
        },
        { status: 422 },
      );
    });

    const result = await apiFetch('/api/auth/login', { method: 'POST', body: {} });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.formError).toBe('Something went wrong.');
    }
  });

  it('throws ReauthRequiredError on 401 REAUTH_REQUIRED', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (url === '/api/auth/csrf') {
        document.cookie = '__Host-XSRF=test-csrf-token; path=/';
        return new Response(null, { status: 204 });
      }
      return jsonResponse(
        { error: { code: 'REAUTH_REQUIRED', message: 'Please reauthenticate.' } },
        { status: 401 },
      );
    });

    await expect(
      apiFetch('/api/auth/mfa/disable', { method: 'POST', body: {} }),
    ).rejects.toBeInstanceOf(ReauthRequiredError);
  });

  it('throws NetworkError when fetch rejects', async () => {
    fetchSpy.mockRejectedValueOnce(new TypeError('Failed to fetch'));

    await expect(apiFetch('/api/auth/me')).rejects.toBeInstanceOf(NetworkError);
  });
});
```

- [ ] **Step 2: Run the failing tests**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/lib/api-client.test.ts
```

Expected: fail (module doesn't exist).

- [ ] **Step 3: Create `csrf.ts`**

Create `ProjectCeres.Client/src/app/auth/csrf.ts`:

```typescript
const COOKIE_NAME = '__Host-XSRF';

export function readXsrfToken(): string | null {
  if (typeof document === 'undefined') return null;
  const match = document.cookie.match(new RegExp(`(?:^|;\\s*)${COOKIE_NAME}=([^;]+)`));
  return match ? decodeURIComponent(match[1]) : null;
}

export function clearXsrfTokenCacheForTests(): void {
  // Reset module state for tests; intentionally a no-op outside tests.
  // (No module state to reset in this minimal helper — kept for forward
  // compatibility if we add a memo later.)
}
```

- [ ] **Step 4: Create `api-client.ts`**

Create `ProjectCeres.Client/src/app/lib/api-client.ts`:

```typescript
import { readXsrfToken } from '../auth/csrf';

export class ReauthRequiredError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ReauthRequiredError';
  }
}

export class NetworkError extends Error {
  constructor(message: string, public cause?: unknown) {
    super(message);
    this.name = 'NetworkError';
  }
}

type ProjectErrorEnvelope = {
  error: {
    code: string;
    message: string;
    details?: Array<{ field: string; message: string }>;
  };
};

export type ApiSuccess<T> = { ok: true; status: number; data: T | null };
export type ApiFailure =
  | { ok: false; status: number; code: string; message: string; fieldErrors?: Record<string, string>; formError?: string };
export type ApiResult<T> = ApiSuccess<T> | ApiFailure;

export type ApiFetchInit = Omit<RequestInit, 'body'> & {
  body?: unknown; // serialised to JSON if not already a string/FormData/Blob
};

const STATE_CHANGING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

let csrfHandshakeInFlight: Promise<void> | null = null;

async function ensureCsrfToken(): Promise<void> {
  if (readXsrfToken()) return;
  if (csrfHandshakeInFlight) return csrfHandshakeInFlight;
  csrfHandshakeInFlight = (async () => {
    try {
      await fetch('/api/auth/csrf', { method: 'GET', credentials: 'include' });
    } finally {
      csrfHandshakeInFlight = null;
    }
  })();
  return csrfHandshakeInFlight;
}

/**
 * Project Ceres SPA fetch wrapper. Handles:
 *  - CSRF handshake (GET /api/auth/csrf) on first state-changing request, deduped
 *  - X-XSRF-TOKEN header on POST/PUT/PATCH/DELETE
 *  - credentials: 'include' so cookies travel
 *  - Project error envelope mapping (422 → field errors / form error,
 *    401 REAUTH_REQUIRED → ReauthRequiredError throw, others → ApiFailure)
 *
 * Generic T is the success body shape; default `unknown` for endpoints
 * returning 204.
 */
export async function apiFetch<T = unknown>(
  url: string,
  init: ApiFetchInit = {},
): Promise<ApiResult<T>> {
  const method = (init.method ?? 'GET').toUpperCase();
  const headers: Record<string, string> = { ...(init.headers as Record<string, string> | undefined) };

  if (STATE_CHANGING_METHODS.has(method)) {
    await ensureCsrfToken();
    const token = readXsrfToken();
    if (token) headers['X-XSRF-TOKEN'] = token;
  }

  let body: BodyInit | undefined;
  if (init.body !== undefined && init.body !== null) {
    if (typeof init.body === 'string' || init.body instanceof FormData || init.body instanceof Blob) {
      body = init.body;
    } else {
      body = JSON.stringify(init.body);
      headers['Content-Type'] = headers['Content-Type'] ?? 'application/json';
    }
  }

  let response: Response;
  try {
    response = await fetch(url, {
      ...init,
      method,
      headers,
      body,
      credentials: 'include',
    });
  } catch (err) {
    throw new NetworkError('Network request failed', err);
  }

  if (response.status === 204) {
    return { ok: true, status: 204, data: null };
  }

  const contentType = response.headers.get('Content-Type') ?? '';
  const isJson = contentType.includes('application/json');
  const payload = isJson ? await response.json().catch(() => null) : null;

  if (response.ok) {
    return { ok: true, status: response.status, data: payload as T };
  }

  // Error path — try to read the project envelope.
  const envelope = (payload as ProjectErrorEnvelope | null)?.error;
  const code = envelope?.code ?? `HTTP_${response.status}`;
  const message = envelope?.message ?? response.statusText;

  if (response.status === 401 && code === 'REAUTH_REQUIRED') {
    throw new ReauthRequiredError(message);
  }

  if (response.status === 422 && envelope) {
    if (envelope.details && envelope.details.length > 0) {
      const fieldErrors: Record<string, string> = {};
      for (const d of envelope.details) {
        fieldErrors[d.field] = d.message;
      }
      return { ok: false, status: 422, code, message, fieldErrors };
    }
    return { ok: false, status: 422, code, message, formError: message };
  }

  return { ok: false, status: response.status, code, message };
}
```

- [ ] **Step 5: Run the api-client tests to verify they pass**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/lib/api-client.test.ts
```

Expected: 7 passes.

- [ ] **Step 6: Write the auth-context tests (failing)**

Create `ProjectCeres.Client/src/app/auth/auth-context.test.tsx`:

```typescript
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider, useAuth } from './auth-context';

function StatusProbe() {
  const auth = useAuth();
  return <div data-testid="status">{auth.status}</div>;
}

function UserProbe() {
  const auth = useAuth();
  return <div data-testid="user-email">{auth.user?.email ?? 'none'}</div>;
}

describe('AuthProvider', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
  });

  afterEach(() => vi.restoreAllMocks());

  it('starts in loading status', () => {
    fetchSpy.mockImplementationOnce(() => new Promise(() => {})); // never resolves
    render(
      <AuthProvider>
        <StatusProbe />
      </AuthProvider>,
    );
    expect(screen.getByTestId('status').textContent).toBe('loading');
  });

  it('transitions to authed and exposes user when /api/auth/me returns 200', async () => {
    fetchSpy.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          userId: '00000000-0000-0000-0000-000000000001',
          email: 'a@b.test',
          twoFactorEnabled: false,
          lastReauthAt: null,
          backupCodesRemaining: 0,
          usedBackupCodeAtLastLogin: false,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    render(
      <AuthProvider>
        <StatusProbe />
        <UserProbe />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('authed'));
    expect(screen.getByTestId('user-email').textContent).toBe('a@b.test');
  });

  it('transitions to anon when /api/auth/me returns 401', async () => {
    fetchSpy.mockResolvedValueOnce(
      new Response(
        JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    render(
      <AuthProvider>
        <StatusProbe />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('anon'));
  });
});
```

- [ ] **Step 7: Write the use-step-up tests (failing)**

Create `ProjectCeres.Client/src/app/auth/use-step-up.test.tsx`:

```typescript
import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useStepUp } from './use-step-up';
import { ReauthRequiredError } from '../lib/api-client';

describe('useStepUp', () => {
  it('returns the action result when no reauth is required', async () => {
    const { result } = renderHook(() => useStepUp());
    const action = async () => 'success';
    await expect(result.current.requireStepUp(action)).resolves.toBe('success');
  });

  it('rethrows ReauthRequiredError when no dialog is mounted (Phase 1 stub)', async () => {
    const { result } = renderHook(() => useStepUp());
    const action = async () => {
      throw new ReauthRequiredError('Please reauthenticate.');
    };
    await expect(result.current.requireStepUp(action)).rejects.toBeInstanceOf(ReauthRequiredError);
  });
});
```

- [ ] **Step 8: Run the failing context + step-up tests**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/auth/auth-context.test.tsx src/app/auth/use-step-up.test.tsx
```

Expected: both fail (modules don't exist).

- [ ] **Step 9: Create `auth-context.tsx`**

Create `ProjectCeres.Client/src/app/auth/auth-context.tsx`:

```typescript
import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { apiFetch } from '../lib/api-client';

export type AuthUser = {
  userId: string;
  email: string;
  twoFactorEnabled: boolean;
  lastReauthAt: number | null;
  backupCodesRemaining: number;
  usedBackupCodeAtLastLogin: boolean;
};

export type AuthStatus = 'loading' | 'anon' | 'authed';

type AuthContextValue = {
  status: AuthStatus;
  user: AuthUser | null;
  refresh: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading');
  const [user, setUser] = useState<AuthUser | null>(null);

  const refresh = useCallback(async () => {
    const result = await apiFetch<AuthUser>('/api/auth/me');
    if (result.ok && result.data) {
      setUser(result.data);
      setStatus('authed');
    } else {
      setUser(null);
      setStatus('anon');
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return (
    <AuthContext.Provider value={{ status, user, refresh }}>
      {children}
    </AuthContext.Provider>
  );
}
```

- [ ] **Step 10: Create `use-step-up.ts`**

Create `ProjectCeres.Client/src/app/auth/use-step-up.ts`:

```typescript
import { useCallback } from 'react';
import { ReauthRequiredError } from '../lib/api-client';

export type UseStepUpResult = {
  /**
   * Run an async action; if it throws ReauthRequiredError, the reauth
   * dialog opens and the action is retried after success. In Phase 1
   * the dialog isn't mounted yet, so ReauthRequiredError just rethrows
   * for the caller to surface — the hook's signature is forward-
   * compatible so Phase 4 only has to wire the dialog state without
   * changing the call sites.
   */
  requireStepUp: <T>(action: () => Promise<T>) => Promise<T>;
};

export function useStepUp(): UseStepUpResult {
  const requireStepUp = useCallback(async <T,>(action: () => Promise<T>): Promise<T> => {
    try {
      return await action();
    } catch (err) {
      if (err instanceof ReauthRequiredError) {
        // Phase 4 wires this to open the modal + retry.
        throw err;
      }
      throw err;
    }
  }, []);
  return { requireStepUp };
}
```

- [ ] **Step 11: Run the context + step-up tests**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/auth/auth-context.test.tsx src/app/auth/use-step-up.test.tsx
```

Expected: 5 passes.

- [ ] **Step 12: Wire `<AuthProvider>` into `main.tsx`**

Open `ProjectCeres.Client/src/app/main.tsx`. Add the import for `AuthProvider`, then wrap `<App />` with it (inside `<BrowserRouter>` so navigation hooks work):

```typescript
import { AuthProvider } from './auth/auth-context';

// ... inside the createRoot(...).render(...):
<StrictMode>
  <ThemeProvider attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange>
    <BrowserRouter basename="/app">
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </ThemeProvider>
</StrictMode>
```

- [ ] **Step 13: Wire the real gate into `RequireAuth.tsx`**

Open `ProjectCeres.Client/src/app/auth/RequireAuth.tsx` and replace the body:

```typescript
import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from './auth-context';

export function RequireAuth({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const location = useLocation();

  if (auth.status === 'loading') {
    // Render nothing while we wait for /api/auth/me. The flash is brief
    // (<100ms typically); a skeleton screen here would be more visual
    // noise than the alternative.
    return null;
  }

  if (auth.status === 'anon') {
    return <Navigate to={`/login?redirect=${encodeURIComponent(location.pathname)}`} replace />;
  }

  return <>{children}</>;
}
```

- [ ] **Step 14: Add the redirect-when-anon test**

Open `ProjectCeres.Client/src/app/auth/RequireAuth.test.tsx`. Add this test alongside the existing one:

```typescript
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RequireAuth } from './RequireAuth';
import { AuthProvider } from './auth-context';

describe('RequireAuth — anonymous redirect', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    fetchSpy.mockResolvedValueOnce(
      new Response(
        JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      ),
    );
  });

  afterEach(() => vi.restoreAllMocks());

  it('redirects anon visitors to /login with the redirect param', async () => {
    render(
      <AuthProvider>
        <MemoryRouter initialEntries={['/dashboard']}>
          <Routes>
            <Route path="/login" element={<div>login page</div>} />
            <Route
              path="/dashboard"
              element={
                <RequireAuth>
                  <div>protected</div>
                </RequireAuth>
              }
            />
          </Routes>
        </MemoryRouter>
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByText('login page')).toBeDefined());
  });
});
```

The other test in the file (the passthrough one from Task 3) needs an update — `RequireAuth` now requires `<AuthProvider>` to be a parent. Wrap its render in `<AuthProvider>` and mock `/api/auth/me` to return a successful user, similar to the auth-context tests.

- [ ] **Step 15: Run all client tests**

```bash
pnpm --dir ProjectCeres.Client test --run
```

Expected: green. Existing AppLayout / route-table tests still pass (the `<AuthProvider>` is now wrapped around `<App />` in `main.tsx` but tests render via `MemoryRouter` directly without going through `main.tsx`).

If existing tests break because they render parts of the app that now use `useAuth()`, wrap their renders in `<AuthProvider>` and mock the `/api/auth/me` fetch as the new tests do. **Do not weaken the failing test** — fix the test setup or the production code, never the assertion.

- [ ] **Step 16: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Client/src/app/lib/api-client.ts \
  ProjectCeres.Client/src/app/lib/api-client.test.ts \
  ProjectCeres.Client/src/app/auth/csrf.ts \
  ProjectCeres.Client/src/app/auth/auth-context.tsx \
  ProjectCeres.Client/src/app/auth/auth-context.test.tsx \
  ProjectCeres.Client/src/app/auth/use-step-up.ts \
  ProjectCeres.Client/src/app/auth/use-step-up.test.tsx \
  ProjectCeres.Client/src/app/auth/RequireAuth.tsx \
  ProjectCeres.Client/src/app/auth/RequireAuth.test.tsx \
  ProjectCeres.Client/src/app/main.tsx

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9): add API client + CSRF handshake + AuthProvider

Stage 9 foundation A3. Three pieces land together because each is
useless without the others:

apiFetch(): single SPA fetch wrapper that runs the project's CSRF
handshake (GET /api/auth/csrf, deduped via module-level promise),
echoes the __Host-XSRF cookie back as the X-XSRF-TOKEN header on
state-changing requests, and maps the project's error envelope into
typed results. 422 with field details → fieldErrors map for
react-hook-form's setError; 422 with empty details → formError for
page-level alerts; 401 REAUTH_REQUIRED → ReauthRequiredError throw;
network failure → NetworkError throw.

AuthProvider: calls /api/auth/me on mount, exposes status (loading /
anon / authed) + user + refresh(). useAuth() throws if used outside
the provider so misuse fails loudly at dev time.

useStepUp: returns requireStepUp(action) that catches
ReauthRequiredError. Phase 1 stub rethrows; Phase 4 wires the dialog
without changing the call sites' signature.

RequireAuth gains the real gate: redirects anon visitors to /login
with a redirect param so post-login navigation lands them where
they were trying to go.
EOF
)"
```

---

## Task 5: Add form library + OTP + QR + ADR-0074 (commit 5)

**Files:**
- Modify: `ProjectCeres.Client/package.json` — add `react-hook-form`, `zod`, `@hookform/resolvers`, `qrcode.react`; add shadcn `input-otp`
- Create: `docs/decisions/ADR-0074-react-hook-form-with-zod-for-spa-forms.md`
- Modify: `docs/planning-phase3.md` — § 14 implementation order updated

**Background:** Spec § 1 foundation piece A4. Adopting `react-hook-form` + `zod` is a project-wide pattern call (the user explicitly chose it in the brainstorm); it deserves an ADR per the bar set by ADR-0033 (Chart.js) and ADR-0072 (Playwright). `qrcode.react` is for the TOTP setup screen (Phase 4); `input-otp` is the shadcn-blessed 6-cell OTP input with proper paste + auto-advance + iOS-no-zoom behaviour.

**Why it lands here, before `/login`:** `/login` uses `react-hook-form` + `zod`; without those installed first, Task 6 can't ship.

**Per-commit doc-sync:** `planning-phase3.md` § 14 (implementation order) gets a one-line update noting the libs are now part of the SPA-form pattern.

- [ ] **Step 1: Add the form + QR npm packages via pnpm**

```bash
pnpm --dir ProjectCeres.Client add react-hook-form zod @hookform/resolvers qrcode.react
```

Confirm `package.json` shows the four new entries.

- [ ] **Step 2: Add shadcn `input-otp` via the project's shadcn CLI**

```bash
pnpm --dir ProjectCeres.Client dlx shadcn@latest add input-otp
```

This creates `ProjectCeres.Client/src/components/ui/input-otp.tsx`. Verify the file exists and is committable. The component already handles paste, auto-advance, and `inputMode="numeric"`; we'll layer the iOS no-zoom guarantee (`font-size: 16px`) at the call site in Phase 3.

- [ ] **Step 3: Run a build to confirm everything resolves**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: green build. Bundle size check (built into the project's build script) should still pass; the new deps add ~25kb gzipped combined which is within budget. If the bundle check fails, surface the result before continuing — the spec calls out this risk in § 7.

- [ ] **Step 4: Run all existing tests to confirm no regression**

```bash
pnpm --dir ProjectCeres.Client test --run
```

Expected: green.

- [ ] **Step 5: Write ADR-0074**

Create `docs/decisions/ADR-0074-react-hook-form-with-zod-for-spa-forms.md`:

```markdown
# ADR-0074 — react-hook-form + zod for SPA forms

## Status: Accepted (2026-05-15, Stage 9 Phase 1)

## Context

Project Ceres' SPA had no form library before Stage 9. Forms were built with plain `useState` per field, ad-hoc validators, and the `<Field>` wrapper at `src/app/components/Field.tsx` for layout. That pattern works for simple forms (a single text input + submit button) but pushes its limits when:

- Multiple fields need cross-field validation (e.g. `password` matches `confirmPassword`).
- Submission needs an in-flight loading state plus a success/failure outcome.
- Field-level errors must move keyboard focus to the first errored field on submit (an a11y requirement called out in `roadmap-phase-three.md:1049`).
- Multi-step forms need state spanning steps (`/security/totp/setup` is enroll → verify → backup-codes-acknowledged).
- Conditional fields appear based on other field values (TOTP code on `/password-reset/confirm` only when the user has TOTP enabled).

Stage 9 ships **nine forms** with these characteristics. Hand-rolling state + validation + focus management + submission per page would mean 80–120 lines of plumbing per form, repeated across nine pages, with no enforcement of consistency.

## Decision

Adopt **`react-hook-form`** for form state, validation, error tracking, focus management, and submission lifecycle. Pair with **`zod`** + **`@hookform/resolvers`** for schema-driven validation that doubles as TypeScript types via `z.infer<typeof schema>`.

Bundle size impact: ~24kB gzipped combined.

## Pattern

Each form has:
- A zod schema in `src/app/auth/schemas/<feature>.schema.ts` (or per-feature folder).
- A page component that calls `useForm({ resolver: zodResolver(schema) })`.
- Inputs registered with `{...register('field')}`.
- Submit handler wrapped in `form.handleSubmit(onSubmit)` so the form is a real `<form onSubmit={...}>` (Enter submits per the project's Rule 4 in `2026-05-15-stage-9-auth-spa-pages-design.md` § 4).
- `<Button type="submit">` (the shadcn `<Button>` defaults to `type="button"`, so the explicit `type="submit"` is required).

## Migration policy

Existing non-auth forms (Movements, Categories, Budgets, Accounts, Recurring, Settings, Profile, Security, Import) **stay on the current `useState + <Field>` pattern**. They are not in the path of Stage 9, and a big-bang migration would introduce churn without immediate value. When any of those pages is touched for unrelated work, the implementer may choose to migrate it as part of that work — but is not required to.

The new pattern applies to:
- Every new form built from Stage 9 onwards.
- Any auth-page form (the entire `src/app/pages/auth/` tree).

## Alternatives considered

- **Plain `useState` + grow the `<Field>` wrapper** — rejected. The four genuinely-complex Stage 9 forms (Register with strength meter + cross-field, TOTP setup multi-step, Password-reset confirm with conditional TOTP, the reauth dialog with conditional input shape) push hand-rolled state past where it pays for itself.
- **Add only `zod` (validation), keep `useState` (state)** — rejected. ~15kB saved but the focus-on-first-error hook would still be hand-rolled, which is the part most likely to drift across pages.

## Consequences

- New top-level deps: `react-hook-form`, `zod`, `@hookform/resolvers`. Plus `qrcode.react` (for TOTP setup) and the shadcn `input-otp` component (for the 6-cell OTP input). All installed via pnpm per `feedback_pnpm_only_never_npm`.
- Project-wide form pattern documented in this ADR; future contributors know which pattern is canonical for new work.
- No migration burden on existing forms; they migrate opportunistically when touched for other reasons.
```

- [ ] **Step 6: Doc-sync — update `planning-phase3.md` § 14**

Add a one-line entry under § 14 (Implementation order) noting that the form pattern (`react-hook-form` + `zod`, see ADR-0074) is established in Stage 9 Phase 1 and applies to all auth-page forms going forward. Run the `sync-docs` skill to find the right location.

- [ ] **Step 7: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Client/package.json \
  ProjectCeres.Client/pnpm-lock.yaml \
  ProjectCeres.Client/components.json \
  ProjectCeres.Client/src/components/ui/input-otp.tsx \
  docs/decisions/ADR-0074-react-hook-form-with-zod-for-spa-forms.md \
  docs/planning-phase3.md

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9): adopt react-hook-form + zod for SPA forms (ADR-0074)

Stage 9 foundation A4. Adds the project-wide SPA form pattern:
react-hook-form for state + focus + submission lifecycle, zod for
validation schemas that double as TypeScript types via z.infer<>.
Plus qrcode.react for the TOTP setup screen (Phase 4) and the
shadcn input-otp component for the 6-cell OTP input.

ADR-0074 documents the choice and the migration policy: new auth
forms use the new pattern; existing non-auth forms stay on the
current useState + <Field> pattern unless touched for other work.

All deps installed via pnpm — never npm.
EOF
)"
```

---

## Task 6: Build `/login` page + globe language toggle + roadmap line 977 rewrite (commit 6)

**Files:**
- Create: `ProjectCeres.Client/src/app/auth/schemas/login.schema.ts`
- Create: `ProjectCeres.Client/src/app/pages/auth/Login.tsx`
- Create: `ProjectCeres.Client/src/app/pages/auth/Login.test.tsx`
- Create: `ProjectCeres.Client/src/app/pages/auth/Login.a11y.test.tsx`
- Create: `ProjectCeres.Client/src/app/components/auth/LanguageToggle.tsx`
- Create: `ProjectCeres.Client/src/app/components/auth/LanguageToggle.test.tsx`
- Modify: `ProjectCeres.Client/src/app/App.tsx` — register `/login` inside the public branch + mount `<LanguageToggle>` in the AuthLayout footer
- Modify: `ProjectCeres.Client/src/app/layout/AuthLayout.tsx` — render `<LanguageToggle>` in the footer slot
- Modify: `docs/roadmap-phase-three.md` — rewrite line 977; tick checklist items 967–976 + 1043–1047

**Background:** Spec § 3 (`/login` row), § 4 (error matrix rules 1–4 + the EMAIL_NOT_CONFIRMED row), § 7 commit 6. The vertical slice's payoff: a real working login page in the user's browser. The `<LanguageToggle>` mounts in the AuthLayout footer slot so it appears below every public-branch page.

**The locked design call from the brainstorm:** login success redirects to `/` regardless of TOTP-enrolment status. The roadmap's existing line 977 says otherwise; this commit rewrites it.

**Per-commit doc-sync:** roadmap line 977 + ticks for 967–976 + 1043–1047, in this same commit.

- [ ] **Step 1: Write the login zod schema (no test needed — the schema is exercised through the page tests)**

Create `ProjectCeres.Client/src/app/auth/schemas/login.schema.ts`:

```typescript
import { z } from 'zod';

export const loginSchema = z.object({
  email: z.string().email('Enter a valid email address.'),
  password: z.string().min(1, 'Password is required.'),
  rememberMe: z.boolean().default(false),
});

export type LoginFormValues = z.infer<typeof loginSchema>;
```

The schema validates **shape**, not **policy** (length, breach screening). Policy lives on the server; the SPA mirrors only what the form needs to render inline before a network round-trip.

- [ ] **Step 2: Write the LanguageToggle test (failing)**

Create `ProjectCeres.Client/src/app/components/auth/LanguageToggle.test.tsx`:

```typescript
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { LanguageToggle } from './LanguageToggle';

describe('LanguageToggle', () => {
  beforeEach(() => {
    document.cookie = 'lang=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
    void i18n.changeLanguage('en');
  });

  afterEach(() => {
    document.cookie = 'lang=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  });

  it('renders the globe button with an accessible label', () => {
    render(
      <I18nextProvider i18n={i18n}>
        <LanguageToggle />
      </I18nextProvider>,
    );
    expect(screen.getByRole('button', { name: /change language/i })).toBeDefined();
  });

  it('switches the language and writes the lang cookie when an option is selected', async () => {
    const user = userEvent.setup();
    render(
      <I18nextProvider i18n={i18n}>
        <LanguageToggle />
      </I18nextProvider>,
    );
    await user.click(screen.getByRole('button', { name: /change language/i }));
    await user.click(screen.getByRole('menuitem', { name: 'Español' }));

    expect(i18n.language).toBe('es');
    expect(document.cookie).toContain('lang=es');
  });
});
```

- [ ] **Step 3: Write the Login page tests (failing)**

Create `ProjectCeres.Client/src/app/pages/auth/Login.test.tsx`:

```typescript
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { Login } from './Login';

function renderLogin(initialPath = '/login') {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={[initialPath]}>
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route path="/" element={<div>dashboard</div>} />
            <Route path="/login/totp" element={<div>totp step</div>} />
            <Route path="/account/unlock" element={<div>unlock page</div>} />
            <Route path="/password-reset" element={<div>password reset request</div>} />
            <Route path="/register" element={<div>register page</div>} />
            <Route path="/movements" element={<div>movements</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('Login page', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    // /api/auth/me returns 401 (anonymous) by default for these tests.
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    document.cookie = '__Host-XSRF=test; path=/';
  });

  afterEach(() => vi.restoreAllMocks());

  it('renders the form with email + password + remember-me + submit', () => {
    renderLogin();
    expect(screen.getByLabelText(/email/i)).toBeDefined();
    expect(screen.getByLabelText(/password/i)).toBeDefined();
    expect(screen.getByLabelText(/remember me/i)).toBeDefined();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeDefined();
  });

  it('renders the forgot-password and create-account links', () => {
    renderLogin();
    expect(screen.getByRole('link', { name: /forgot password/i })).toBeDefined();
    expect(screen.getByRole('link', { name: /create an account/i })).toBeDefined();
  });

  it('submits via Enter key from the password field', async () => {
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw{Enter}');

    // After /api/auth/me's initial 401, the next fetch is to /api/auth/login.
    await waitFor(() =>
      expect(fetchSpy).toHaveBeenCalledWith(
        '/api/auth/login',
        expect.objectContaining({ method: 'POST' }),
      ),
    );
  });

  it('on success without TOTP redirects to / regardless of TOTP status', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText('dashboard')).toBeDefined());
  });

  it('on requiresTotp:true redirects to /login/totp', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(JSON.stringify({ requiresTotp: true }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText('totp step')).toBeDefined());
  });

  it('renders an inline field error on INVALID_CREDENTIALS', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(
          JSON.stringify({ error: { code: 'INVALID_CREDENTIALS', message: 'Invalid email or password.' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText(/email or password is incorrect/i)).toBeDefined());
  });

  it('redirects to /account/unlock on ACCOUNT_LOCKED_OUT', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(
          JSON.stringify({ error: { code: 'ACCOUNT_LOCKED_OUT', message: 'Locked.' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText('unlock page')).toBeDefined());
  });

  it('renders the resend-verification link on EMAIL_NOT_CONFIRMED', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(
          JSON.stringify({ error: { code: 'EMAIL_NOT_CONFIRMED', message: 'Verify first.' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /resend verification email/i })).toBeDefined(),
    );
  });

  it('honours the redirect query param on success', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin('/login?redirect=%2Fmovements');
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText('movements')).toBeDefined());
  });
});
```

- [ ] **Step 4: Write the Login a11y test (failing)**

Create `ProjectCeres.Client/src/app/pages/auth/Login.a11y.test.tsx`:

```typescript
import { render } from '@testing-library/react';
import { afterEach, beforeEach, describe, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { AuthLayout } from '../../layout/AuthLayout';
import { Login } from './Login';
import { expectNoA11yViolations } from '../../lib/test-axe';

describe('Login a11y', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    fetchSpy.mockResolvedValue(
      new Response(
        JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      ),
    );
  });

  afterEach(() => vi.restoreAllMocks());

  it('renders without serious or critical axe violations', async () => {
    const { container } = render(
      <I18nextProvider i18n={i18n}>
        <AuthProvider>
          <MemoryRouter initialEntries={['/login']}>
            <Routes>
              <Route element={<AuthLayout />}>
                <Route path="login" element={<Login />} />
              </Route>
            </Routes>
          </MemoryRouter>
        </AuthProvider>
      </I18nextProvider>,
    );
    await expectNoA11yViolations(container);
  });
});
```

- [ ] **Step 5: Run the failing tests**

```bash
pnpm --dir ProjectCeres.Client test --run \
  src/app/pages/auth/Login.test.tsx \
  src/app/pages/auth/Login.a11y.test.tsx \
  src/app/components/auth/LanguageToggle.test.tsx
```

Expected: all fail (modules don't exist).

- [ ] **Step 6: Create `<LanguageToggle>`**

Create `ProjectCeres.Client/src/app/components/auth/LanguageToggle.tsx`:

```typescript
import { Globe } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { writeLangCookie, type SupportedLanguage } from '../../i18n/i18n';

export function LanguageToggle() {
  const { t, i18n } = useTranslation();

  const choose = async (lang: SupportedLanguage) => {
    await i18n.changeLanguage(lang);
    writeLangCookie(lang);
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button variant="ghost" size="sm" type="button" aria-label={t('auth.languageToggle.ariaLabel')}>
            <Globe className="h-4 w-4" />
          </Button>
        }
      />
      <DropdownMenuContent align="center">
        <DropdownMenuItem onClick={() => void choose('en')}>
          {t('auth.languageToggle.english')}
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => void choose('es')}>
          {t('auth.languageToggle.spanish')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

**API note:** the project uses `@base-ui/react` under the hood (per `verify-against-codebase` findings), not Radix. That means `<DropdownMenuTrigger render={<Button ...>}>` not Radix's `asChild`. If the existing `dropdown-menu.tsx` in `src/components/ui/` uses a different prop name, match what the file exports.

- [ ] **Step 7: Mount `<LanguageToggle>` in the AuthLayout footer**

Open `ProjectCeres.Client/src/app/layout/AuthLayout.tsx`. Replace the empty `<footer>` with:

```typescript
import { LanguageToggle } from '../components/auth/LanguageToggle';

// ... inside the JSX ...
<footer className="flex justify-center" data-slot="auth-footer">
  <LanguageToggle />
</footer>
```

- [ ] **Step 8: Create the `Login` page**

Create `ProjectCeres.Client/src/app/pages/auth/Login.tsx`:

```typescript
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Field } from '../../components/Field';
import { apiFetch } from '../../lib/api-client';
import { useAuth } from '../../auth/auth-context';
import { loginSchema, type LoginFormValues } from '../../auth/schemas/login.schema';

type ServerErrorState =
  | { kind: 'none' }
  | { kind: 'invalidCredentials' }
  | { kind: 'emailNotConfirmed' };

export function Login() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const auth = useAuth();
  const [serverError, setServerError] = useState<ServerErrorState>({ kind: 'none' });
  const [resending, setResending] = useState(false);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
    setError,
  } = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '', rememberMe: false },
  });

  const onSubmit = async (values: LoginFormValues) => {
    setServerError({ kind: 'none' });
    try {
      const result = await apiFetch<{ requiresTotp?: boolean }>('/api/auth/login', {
        method: 'POST',
        body: values,
      });

      if (result.ok) {
        if (result.data?.requiresTotp) {
          navigate('/login/totp');
          return;
        }
        await auth.refresh();
        const redirectTo = searchParams.get('redirect') ?? '/';
        navigate(redirectTo);
        return;
      }

      // Failure path — map known codes to UX.
      if (result.code === 'ACCOUNT_LOCKED_OUT') {
        navigate('/account/unlock');
        return;
      }
      if (result.code === 'EMAIL_NOT_CONFIRMED') {
        setServerError({ kind: 'emailNotConfirmed' });
        return;
      }
      if (result.code === 'INVALID_CREDENTIALS') {
        setServerError({ kind: 'invalidCredentials' });
        setError('password', { type: 'server', message: t('auth.login.errors.invalidCredentials') });
        return;
      }
      if (result.fieldErrors) {
        for (const [field, message] of Object.entries(result.fieldErrors)) {
          setError(field as keyof LoginFormValues, { type: 'server', message });
        }
        return;
      }
      // Unknown failure — surface as a generic field error on password (the
      // safer of the two — never leak email-specific information).
      setError('password', { type: 'server', message: result.message });
    } catch (err) {
      // Network / 5xx — toast handling lives in a shared error boundary in
      // a later commit; for now, surface a generic password field error.
      setError('password', { type: 'server', message: t('auth.login.errors.invalidCredentials') });
    }
  };

  const onResendVerification = async () => {
    setResending(true);
    try {
      // Resend endpoint ships in Phase 2 (commit 9); this is a placeholder
      // call that will succeed once that endpoint exists. The button itself
      // should ship now so the EMAIL_NOT_CONFIRMED UX is complete the
      // moment Phase 2 lands.
      await apiFetch('/api/auth/email/verify/resend', { method: 'POST', body: {} });
    } finally {
      setResending(false);
    }
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.login.title')}</h1>

      <Field label={t('auth.login.emailLabel')} htmlFor="email" error={errors.email?.message}>
        <Input
          id="email"
          type="email"
          autoComplete="email"
          autoFocus
          {...register('email')}
        />
      </Field>

      <Field label={t('auth.login.passwordLabel')} htmlFor="password" error={errors.password?.message}>
        <Input
          id="password"
          type="password"
          autoComplete="current-password"
          {...register('password')}
        />
      </Field>

      {serverError.kind === 'emailNotConfirmed' && (
        <div className="text-sm" role="status">
          <Button
            type="button"
            variant="link"
            onClick={onResendVerification}
            disabled={resending}
            className="px-0"
          >
            {t('auth.login.errors.resendVerification')}
          </Button>
        </div>
      )}

      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" {...register('rememberMe')} />
        {t('auth.login.rememberMeLabel')}
      </label>

      <Button type="submit" className="w-full" disabled={isSubmitting}>
        {isSubmitting ? t('auth.login.submitting') : t('auth.login.submit')}
      </Button>

      <div className="flex items-center justify-between text-sm">
        <Link to="/password-reset" className="underline-offset-4 hover:underline">
          {t('auth.login.forgotPasswordLink')}
        </Link>
        <Link to="/register" className="underline-offset-4 hover:underline">
          {t('auth.login.createAccountLink')}
        </Link>
      </div>
    </form>
  );
}
```

- [ ] **Step 9: Register `/login` in the public branch of `App.tsx`**

Open `ProjectCeres.Client/src/app/App.tsx`. Add the `Login` import alongside the others, and the route inside the public branch:

```typescript
import { Login } from './pages/auth/Login';

// ... inside the public-branch <Route element={<AuthLayout />}>:
<Route path="login" element={<Login />} />
```

- [ ] **Step 10: Run all failing tests to verify they now pass**

```bash
pnpm --dir ProjectCeres.Client test --run \
  src/app/pages/auth/Login.test.tsx \
  src/app/pages/auth/Login.a11y.test.tsx \
  src/app/components/auth/LanguageToggle.test.tsx
```

Expected: all green.

- [ ] **Step 11: Run the full client test suite**

```bash
pnpm --dir ProjectCeres.Client test --run
```

Expected: green. Build passes. Existing tests still pass.

- [ ] **Step 12: Build to confirm bundle size still in budget**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: green build, bundle-size check passes.

- [ ] **Step 13: Doc-sync — rewrite roadmap line 977 + tick checklist items**

Open `docs/roadmap-phase-three.md`. Find the current line 977 (under Stage 9.1's `/login` checklist). The current text says (paraphrased): *"On success without TOTP enrolled: redirect to `/login/totp/setup` (first-login enrolment grace path)."* Replace with:

```markdown
- [x] On success: redirect to `/` (dashboard) regardless of TOTP-enrolment status. Per [ADR-0069](decisions/ADR-0069-mfa-opt-in-for-personal-users.md), MFA enrollment is reachable only from Settings → Security; there is no first-login redirect to TOTP setup.
```

In the same diff, tick all of items 967–976 (the `/login` checklist) and 1043–1047 (the language-toggle checklist) that are now satisfied — Stage 9.1 + Stage 9.9-partial are complete with this commit.

Run the `sync-docs` skill against the diff to catch any other roadmap items that need updating.

- [ ] **Step 14: Manual UX verification (the user runs this — do not skip)**

Surface this checklist to the user. The implementer agent does not perform these checks; the user does, in a real browser.

```
Please verify Stage 9 Phase 1 in your browser before we close commit 6:

1. Start the dev server: pnpm --dir ProjectCeres.Client dev (and dotnet run --project ProjectCeres in another terminal).
2. Open https://localhost:<port>/app/login in:
   a. Desktop browser, light mode — golden path: log in with a confirmed test user.
   b. Desktop browser, dark mode — same.
   c. Mobile (Chrome devtools 375px iPhone SE) — confirm:
      - card fills viewport with comfortable padding
      - no horizontal overflow
      - touch targets ≥ 44×44px
      - language toggle reachable without scrolling
   d. Real iOS Safari at 375px (separate, important — emulator does not catch focus-zoom):
      - tap the email field → screen does not zoom
3. Failure cases — try each and confirm the error renders inline next to the right field, not as a toast:
   - wrong password
   - unknown email (still shows the same generic "Email or password is incorrect")
   - locked account (six wrong passwords in a row → redirected to /account/unlock)
4. Language toggle:
   - click globe → choose Español → form copy switches in place, no reload
   - hard refresh → still in Spanish (cookie persisted)
   - DevTools → Application → Cookies → confirm `lang` cookie is `Path=/`, `Secure`, `SameSite=Lax`, NOT HttpOnly, ~1-year expiry
5. Accessibility quick check: tab through the form. Order should be email → password → remember-me → submit → forgot-password → create-account. The language toggle is reachable in the footer.

NOTE — known Phase-1 limitation: clicking "Resend verification email" (visible after an EMAIL_NOT_CONFIRMED login error) will fail with a 404 until Phase 2 ships the resend endpoint. The button is wired now so the UX is complete the moment Phase 2 lands. Do not flag this as a defect during the Phase 1 UX review.

If any of the above fails, do not close commit 6 — surface the failure and revise.
```

The user reports back; if anything needs revision, fix it as a small follow-up commit before commit 6's logical completion.

- [ ] **Step 15: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Client/src/app/auth/schemas/login.schema.ts \
  ProjectCeres.Client/src/app/pages/auth/Login.tsx \
  ProjectCeres.Client/src/app/pages/auth/Login.test.tsx \
  ProjectCeres.Client/src/app/pages/auth/Login.a11y.test.tsx \
  ProjectCeres.Client/src/app/components/auth/LanguageToggle.tsx \
  ProjectCeres.Client/src/app/components/auth/LanguageToggle.test.tsx \
  ProjectCeres.Client/src/app/layout/AuthLayout.tsx \
  ProjectCeres.Client/src/app/App.tsx \
  docs/roadmap-phase-three.md

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9.1): /login page + globe language toggle

The vertical-slice payoff for Phase 1. /login renders inside
<AuthLayout>, uses react-hook-form + zod for state + validation +
focus management, calls apiFetch('/api/auth/login') with the CSRF
handshake, and routes per the spec's matrix:
  - 204 success → / (dashboard) regardless of TOTP status
  - 200 requiresTotp:true → /login/totp
  - 401 INVALID_CREDENTIALS → field error on password
  - 401 ACCOUNT_LOCKED_OUT → /account/unlock
  - 401 EMAIL_NOT_CONFIRMED → field error + inline Resend button

The Resend button calls /api/auth/email/verify/resend which doesn't
exist until Phase 2 — wiring it now keeps the UX complete the moment
that endpoint ships.

Honours ?redirect=<path> from RequireAuth's pre-login redirect.

<LanguageToggle> mounts in the AuthLayout footer; switches in place
via i18n.changeLanguage() and writes the lang cookie. Reachable from
every public-branch page.

Roadmap line 977 rewritten in the same commit per the brainstorm's
locked design call: login lands on / regardless of TOTP status; MFA
is opt-in per ADR-0069 and reachable only from Settings → Security.
Checklist items 967–976 + 1043–1047 ticked.

Phase 1 ends here. Pause for UX review before Phase 2 begins.
EOF
)"
```

---

## Self-Review

### 1. Spec coverage

Mapping each spec § 7 Phase 1 commit to the tasks above:

- Spec commit 1 (`GET /api/auth/me`) → Task 1 ✅
- Spec commit 2 (i18n + `lang` cookie) → Task 2 ✅
- Spec commit 3 (`<AuthLayout>` + routing split) → Task 3 ✅
- Spec commit 4 (API client + CSRF + AuthContext) → Task 4 ✅
- Spec commit 5 (form library + OTP + QR + ADR-0074) → Task 5 ✅
- Spec commit 6 (`/login` + globe toggle + roadmap line 977 rewrite) → Task 6 ✅

Spec § 4 error matrix rules 1–4 are exercised by Task 6's tests (rules 1, 2, 4 directly; rule 3 — lockout-still-recoverable — lives implicitly in the `/account/unlock` redirect, which is the user-visible half of the rule).

Spec § 6 testing layer 1 (server xUnit) is covered by Task 1's MeEndpointTests; layers 2 (vitest + a11y) by Tasks 2–6 throughout; layer 3 (manual UX checklist) by Task 6 step 14.

Spec § 7 Phase 1 doc-sync items: ADR-0074 in Task 5, planning-phase3 § 14 in Task 5, planning-phase3-spa-migration § 8 in Task 2, planning-phase3-spa-migration § 2 in Task 3, roadmap line 977 + checklist ticks in Task 6.

The spec's "first-login TOTP enrolment grace path is not happening" is captured by Task 6's roadmap line 977 rewrite + the Login page's no-grace-path redirect logic.

### 2. Placeholder scan

- "TBD" / "TODO" / "fill in details" — none in the plan.
- "Add appropriate error handling" — every error code is enumerated in Task 6's `onSubmit` switch; no hand-waving.
- "Implement later" — Phase 2/3/4 work is genuinely out of scope; mentioned only as deferral pointers (the `<BackupCodeBanner>` consuming `usedBackupCodeAtLastLogin`, the reauth dialog the `useStepUp` hook will eventually open). These are correct deferrals to later plan files, not placeholders inside this plan.
- One soft area: Task 6's `onResendVerification` calls an endpoint that doesn't exist until Phase 2. The button is wired now so the UX is complete the moment Phase 2's resend endpoint ships; but **before Phase 2 lands, clicking it surfaces a 404**. This is mentioned in the Login.tsx code comment but not in the user-facing UX checklist. **Fixing inline:** add to Task 6 step 14's manual UX list:

> *Note: clicking "Resend verification email" will fail with a 404 until Phase 2 ships the resend endpoint (commit 9). This is expected — the button is wired now so the UX is complete the moment Phase 2 lands. Do not flag this as a defect during the Phase 1 UX review.*

(I'll edit this into the file inline after self-review.)

### 3. Type consistency

- `MeResponse` server DTO ↔ `AuthUser` client type: field-by-field match (`userId`, `email`, `twoFactorEnabled`, `lastReauthAt`, `backupCodesRemaining`, `usedBackupCodeAtLastLogin`). ✅
- `apiFetch` return type `ApiResult<T>` ↔ Login page's `result.ok / result.code / result.fieldErrors / result.message` usage: matches. ✅
- `loginSchema` field names ↔ `<input {...register('email')}>` etc.: match. ✅
- `useStepUp().requireStepUp()` signature ↔ called only from Phase 4 onwards (no Phase 1 caller); the test in Task 4 confirms the rethrow path. ✅
- `AuthStatus` values `'loading' | 'anon' | 'authed'` used identically in `auth-context.tsx`, `RequireAuth.tsx`, and the test files. ✅

One inconsistency caught: my Task 6 description says the `<Button>` defaults to `type="button"` — verified earlier in the verify-against-codebase pre-flight as a real shadcn-base-nova foot-gun — but I didn't add an `enter-submits.test.ts` lint guardrail to Phase 1. The spec § 6 calls it out as a foundation test. **Fixing inline:** I'll add a small addition to Task 4 (since that's where the foundation test bucket lives) noting that the lint guardrail ships in Phase 2 alongside the `<Register>` page where the second form lands; for Phase 1, the `Login.test.tsx` "submits via Enter" test at Step 3 covers the Enter behaviour for the only form that exists. (Not a fix-now; a tracking note.)

Applying the two inline fixes now.

### Inline fixes applied

Two issues found in self-review, both fixed in-file before commit:

1. **Phase-1 limitation surfaced in Task 6 manual UX list.** The "Resend verification email" link in `Login.tsx` calls an endpoint that doesn't ship until Phase 2 (commit 9). Clicking it during the Phase 1 UX review will return 404. Added a NOTE under Task 6 step 14's manual checklist so the user doesn't flag it as a defect.
2. **Enter-submits lint guardrail tracked but not yet shipped.** The spec § 6 calls for a guardrail asserting every `<Button>` inside a `<form>` in the auth pages has explicit `type="submit"` or `type="button"` (the shadcn `<Button>` defaults to `type="button"` and would silently break Enter-to-submit otherwise). Phase 1 has only one form (`<Login>`), and Task 6's Login test "submits via Enter key from the password field" covers the behaviour for that form directly. The project-wide lint guardrail ships in Phase 2 alongside the `<Register>` page where the second auth form lands. **Action for Phase 2 plan-writing:** add the lint test there, not here.

