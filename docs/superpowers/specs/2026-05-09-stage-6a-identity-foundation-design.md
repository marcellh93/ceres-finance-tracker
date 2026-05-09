# Stage 6a — Identity infrastructure foundation (design)

**Date:** 2026-05-09
**Phase:** 3 — Hosted Beta
**Roadmap reference:** `docs/roadmap-phase-three.md` § Stage 6 (Identity infrastructure, Batch 3b)
**Sub-stages covered:** 6.1, 6.2, 6.3, 6.5, 6.6, 6.7
**Sub-stages deferred:**
- Stage 6b — TOTP, lockout, rate limiting, failed-login log (6.4, 6.8, 6.9, 6.10)
- Stage 6c — Password reset, email-change, reauth middleware, audit log (6.11–6.14)

---

## Goal

Wire ASP.NET Core Identity with hardened options, replace the default password hasher with Argon2id pinned to OWASP minimums, ship the `UserSession` + `UserBlockedIp` schema and revocation lookup, register the CSRF middleware (XSRF-TOKEN double-submit pattern), enforce a global authorization fallback policy, and deliver the bare-minimum JSON auth endpoints required to exercise the pipeline end-to-end. **No UI; all verification via xUnit + `WebApplicationFactory`.**

The merge of Stage 6a is intentionally a hard cutover — once it lands, every authenticated endpoint requires a real user. The dev environment is unusable until Stage 6c ships full registration. This is by design and creates the right pressure to ship 6b → 6c without leaving 6a in a half-state.

---

## Locked decisions

The following were resolved during brainstorming on 2026-05-09 and are not re-litigated in implementation:

| Decision | Choice |
|---|---|
| Identity user key type | `ApplicationUser : IdentityUser<Guid>` — matches existing `Guid UserId` columns. |
| Argon2id library | `Konscious.Security.Cryptography.Argon2` (pure-managed, MIT, no native deps). |
| Argon2id parameters | `m=19456, t=2, p=1` (`security-model.md` § Passwords; OWASP baseline). |
| Password policy | Min 8 chars when MFA is enrolled, min 15 chars pre-MFA. **No composition rules.** HIBP screening on set/change. (Per NIST SP 800-63B-4 final 2025-07-31 + OWASP Authentication Cheat Sheet 2026.) |
| FK from `UserId` columns to `AspNetUsers` | **Deferred to Stage 7** — added in the same migration as the sentinel-to-real-user remap (ADR-0066). |
| Session ID storage | Custom claim in the auth cookie + `IUserClaimsPrincipalFactory`; `OnValidatePrincipal` looks up `UserSession` row by `SessionId`. |
| Remember-me cookie model | Two cookies: `__Host-Session` (short, sliding) and `__Host-Persist` (long-lived, rotated on each use). |
| Sentinel-fallback during transition | **None.** `HttpContextCurrentUserAccessor` is the only registered `ICurrentUserAccessor` once 6a merges. |
| Auth endpoints in 6a | Bare-minimum JSON only: `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`. No UI. |
| E2E test infrastructure | **Deferred to Stage 9** (first stage with real auth UI). 6a verifies via xUnit `WebApplicationFactory`. |
| `IgnoreAntiforgeryToken` carve-outs | **None.** CSRF only validates state-changing methods (`POST/PUT/PATCH/DELETE`); GETs are exempt by definition (RFC 9110 safe methods). No endpoint requires the bypass attribute. |

---

## Architecture

### 1. Packages

Add to `ProjectCeres.csproj`:

- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (Identity stores, matches .NET 10 SDK).
- `Konscious.Security.Cryptography.Argon2` (Argon2id primitive).

No other dependencies introduced in 6a.

### 2. Identity wiring

`ApplicationUser : IdentityUser<Guid>` — minimal subclass at first, fields added in 6b/6c (e.g. `MfaEnabled`, `MfaEnrolledAt`).

`AppDbContext` is converted from `DbContext` to `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. All existing `DbSet<>` declarations remain. The `OnModelCreating` chain calls `base.OnModelCreating(builder)` first to register the Identity tables.

A new EF migration `AddIdentitySchema` creates:
- `AspNetUsers` (PK: `Guid Id`)
- `AspNetRoles` (PK: `Guid Id`)
- `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims` — Identity defaults.

The migration **does not** add foreign keys from existing `UserId` columns (`Movement`, `Budget`, `Account`, etc.) to `AspNetUsers`. The sentinel `00000000-0000-0000-0000-000000000001` does not exist in `AspNetUsers` yet, so an FK would fail. Stage 7's remap migration adds the FKs after replacing the sentinel.

`Program.cs` registers Identity:

```csharp
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;

        // Lockout policy lives here in 6a; failed-login *logging* table is 6b.
        options.Lockout.MaxFailedAccessAttempts = 10;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;

        // Password policy: NIST SP 800-63B-4 + OWASP — length only, no composition rules.
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});
```

A custom password validator (`Pre-MfaLengthPasswordValidator`) is added to enforce the 15-character minimum **only when the user does not yet have MFA enrolled**. Once `ApplicationUser.MfaEnabled` is true (set in 6b), the validator falls back to the 8-character floor. The HIBP breach-screening validator is a separate `IPasswordValidator<ApplicationUser>` registered in the same chain.

### 3. Argon2id password hasher

New class `Argon2idPasswordHasher : IPasswordHasher<ApplicationUser>` registered as the default `IPasswordHasher<>` (replaces Identity's PBKDF2 default).

Pinned parameters as `const` fields:

```csharp
private const int MemorySizeKb   = 19456; // 19 MiB
private const int Iterations     = 2;
private const int Parallelism    = 1;
private const int SaltLengthBytes = 16;
private const int HashLengthBytes = 32;
```

Hash output is the canonical PHC string: `$argon2id$v=19$m=19456,t=2,p=1$<base64-salt>$<base64-hash>`. This format stores the algorithm and parameters alongside the hash, supporting future cryptographic agility.

`HashPassword(user, password)` generates a 16-byte cryptographically-random salt, runs Argon2id, returns the PHC string.

`VerifyHashedPassword(user, hashedPassword, providedPassword)`:
1. Parses the PHC string. If parameters don't match the current pinned values → return `PasswordVerificationResult.SuccessRehashNeeded` after a successful match.
2. Re-runs Argon2id on `providedPassword` with the parsed params and compares using `CryptographicOperations.FixedTimeEquals`.
3. Returns `Success`, `SuccessRehashNeeded`, or `Failed`.

A public method `RunDummyHash()` runs Argon2id on a fixed throwaway input — used by the login endpoint when the user is not found, so that timing-based account enumeration is impossible. (Stage 6c reuses this in the password-reset endpoint.)

Identity itself calls `IPasswordHasher.VerifyHashedPassword`; the `SuccessRehashNeeded` return automatically triggers `UserManager` to re-hash and persist on next successful login. No explicit upgrade code needed.

### 4. UserSession and UserBlockedIp

New entities + EF migration `AddUserSessionAndBlockedIp`:

```csharp
public sealed class UserSession
{
    public Guid Id { get; set; }                      // = SessionId claim value
    public Guid UserId { get; set; }                  // FK added Stage 7
    public string? PersistentTokenHash { get; set; }  // Argon2id hash; null for non-persistent
    public string IpCreatedAt { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsPersistent { get; set; }
}

public sealed class UserBlockedIp
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string IpAddress { get; set; } = "";
    public DateTime BlockedAt { get; set; }
    public string? Reason { get; set; }
}
```

Indexes: `UserSession (UserId, RevokedAt)`, `UserSession (LastUsedAt)` for purge queries. `UserBlockedIp (UserId, IpAddress)` unique.

`User-Agent` retention: capped at 90 days per `security-model.md` § Sensitive Fields at Rest. A `IUserJobRunner` purge job is scheduled in Stage 7; for 6a the column simply exists.

#### Cookie configuration

`__Host-Session` (Identity cookie via `AddIdentity` defaults override):

```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "__Host-Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    // No Domain — required by __Host- prefix
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.LoginPath = PathString.Empty;            // SPA handles redirect
    options.AccessDeniedPath = PathString.Empty;
    options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = SessionRevocationValidator.ValidateAsync;
});
```

`__Host-Persist` is a separate authentication scheme registered in `Program.cs`:

```csharp
builder.Services.AddAuthentication()
    .AddScheme<PersistentCookieOptions, PersistentCookieHandler>("PersistentCookie", _ => { });
```

The handler's `HandleAuthenticateAsync` reads the `__Host-Persist` cookie value, hashes it with the same Argon2id hasher, and looks up `UserSession` rows where `PersistentTokenHash` matches and `RevokedAt IS NULL`. Comparison uses PHC `VerifyHashedPassword` semantics (constant-time). On match: rotate the token (issue new 256-bit token, hash it, replace `PersistentTokenHash`, set new `__Host-Persist` cookie), insert a fresh `UserSession` row for the rotated session, sign the user in via the regular Identity scheme (which sets `__Host-Session`).

Persistent cookie attributes: `__Host-Persist`, `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, 30-day rolling expiry.

#### SessionRevocationValidator

```csharp
public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
{
    var sessionIdClaim = ctx.Principal?.FindFirstValue("sid");
    if (!Guid.TryParse(sessionIdClaim, out var sessionId))
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync();
        return;
    }

    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
    var session = await db.UserSessions
        .Where(s => s.Id == sessionId && s.RevokedAt == null)
        .FirstOrDefaultAsync();

    if (session is null)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync();
        return;
    }

    session.LastUsedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
}
```

`LastUsedAt` is updated on every authenticated request. This is one extra DB round-trip per request — acceptable for a personal-finance app at solo-developer scale.

> ⚠ **Performance trade-off flagged for future review.** Every authenticated request hits the DB for a `UserSession` lookup + `LastUsedAt` write. At low traffic this is irrelevant; under load, the write amplification on `UserSession` can dominate the hot path. Mitigations to consider when (and if) it matters:
> - In-memory cache of `(SessionId → UserSession)` with periodic flush of `LastUsedAt` (e.g. flush every 60s or on logout).
> - Drop the per-request `LastUsedAt` write entirely and recompute idle expiry from `Identity`'s `IssuedUtc` claim — sacrifices "last seen" granularity in the active-sessions UI.
> - Replace the EF query with a hand-rolled `SELECT 1 FROM "UserSessions" WHERE "Id" = @id AND "RevokedAt" IS NULL` followed by an unconditional `UPDATE` — bypasses EF change-tracking overhead.
>
> No mitigation is taken in 6a. Revisit when (a) production traffic justifies measurement or (b) request-latency profiling shows the validator as a top-N consumer. Tracked as future-work; not a Stage 6 blocker.

#### IUserClaimsPrincipalFactory

A custom `ApplicationUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>` overrides `GenerateClaimsAsync` to add a `"sid"` claim. The `SessionId` (and the IP/UA/IsPersistent fields for the new `UserSession` row) is passed through `HttpContext.Items` from the login endpoint to the factory.

#### IP block enforcement middleware

`UserBlockedIpMiddleware` runs **after** authentication. For an authenticated request:
1. Resolve client IP from `HttpContext.Connection.RemoteIpAddress` (forwarded-headers middleware must run first per `planning-phase3.md` § Forwarded headers).
2. Query `UserBlockedIp` for `(UserId == currentUser.UserId, IpAddress == clientIp)`.
3. If a row exists → revoke all `UserSession` rows for this user with `IpCreatedAt == clientIp`, return 403.

The middleware reads from a 60-second `IMemoryCache` keyed by `(UserId, IpAddress)` to avoid a per-request DB hit; cache is invalidated when the user adds an IP block (out of scope for 6a — for 6a the cache reads only).

Per-session IP enforcement (the toggle from ADR-0019 §2) is **read** in 6a but the toggle's UI lives on a future preferences entity. For 6a:
- The `UserSession.IpCreatedAt` column is populated on session creation.
- The middleware reads a feature flag `Authentication:IpEnforcement:DefaultEnabled` (defaults to `false` in `appsettings.json`) and, when on, rejects requests where `clientIp != session.IpCreatedAt`.
- The user-level toggle is added in Stage 6b or 6c when the preferences entity exists.

### 5. CSRF middleware

```csharp
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-XSRF";
    options.Cookie.HttpOnly = false;          // SPA must read this
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.HeaderName = "X-XSRF-TOKEN";
});
```

A global filter is registered:

```csharp
builder.Services.Configure<MvcOptions>(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});
```

`AutoValidateAntiforgeryTokenAttribute` validates only on `POST/PUT/PATCH/DELETE`. `GET/HEAD/OPTIONS` are exempt by HTTP definition (RFC 9110 safe methods). **No `[IgnoreAntiforgeryToken]` attributes are added anywhere in 6a.**

CSRF token rotation: explicit calls to `IAntiforgery.GetAndStoreTokens(HttpContext)` immediately after `SignInAsync` (login) and `SignOutAsync` (logout). This issues a fresh `__Host-XSRF` cookie and prevents token reuse across session boundaries.

The React client's existing `apiFetch` helper is extended to:
1. Read the `__Host-XSRF` cookie value (`document.cookie` parsing).
2. Include `X-XSRF-TOKEN: <value>` in the headers of every `POST/PUT/PATCH/DELETE` request.

This is a single-file change in `ProjectCeres.Client/src/lib/apiFetch.ts` (or wherever the helper currently lives).

#### GET safety architecture test

To preserve the invariant that GETs never mutate state — without which CSRF protection silently regresses — add an architecture test:

```csharp
[Fact]
public void HttpGet_actions_must_not_have_write_verb_names()
{
    var forbiddenPrefixes = new[]
    {
        "Create", "Update", "Delete", "Remove",
        "Archive", "Deactivate", "Disable", "Enable", "Restore",
        "Reset", "Submit", "Send", "Process",
        "Approve", "Reject", "Confirm", "Dispute", "Cancel",
        "Upload", "Set"
    };

    var violations = typeof(Program).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        .Where(m => m.GetCustomAttribute<HttpGetAttribute>() is not null)
        .Where(m => forbiddenPrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal)))
        .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
        .ToList();

    violations.Should().BeEmpty(because: "GET endpoints must be side-effect-free per RFC 9110");
}
```

The forbidden-prefix list was derived by sampling existing `Controllers/` action names plus the standard CRUD/lifecycle verbs from the .NET MVC convention. It is **English-only** — and that is correct for this codebase, not an omission to fix.

> **Why the architecture test is English-only:** C# method names are language-level identifiers compiled into IL; they are not user-facing strings and are not subject to localization. Microsoft's MVC documentation, every `dotnet new` template, and the entire .NET ecosystem use English PascalCase for action names. This codebase already follows that convention without exception (every controller in `Controllers/` was sampled). Internationalization in REST APIs applies to *content* — `Accept-Language` headers, response payloads, error messages — never to method or class identifiers (per Google Cloud's API design guidance and RFC 9110 § 12 Content Negotiation). The hazard the test prevents is "a developer adds `[HttpGet] Delete(...)` because that's how a legacy app worked." The defence against the (unlikely, hypothetical) "non-English write-verb method name" is the convention itself, not a regex of multilingual verbs.

### 6. Global authorization fallback policy

```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

`[AllowAnonymous]` whitelist (the only places it appears in 6a):

| Endpoint | Method | Why anonymous |
|---|---|---|
| `/health` | GET | Liveness probe — must work pre-auth. |
| `/api/auth/register` | POST | User cannot register if registration requires login. |
| `/api/auth/login` | POST | Same logic. |
| SPA static catch-all (`/`, `/assets/*`, `/index.html`) | GET | Static assets served before login UI loads. Auth is enforced at the API layer, not the SPA shell. |

Logout (`/api/auth/logout`) is **not** in the whitelist — it requires an authenticated session by definition.

`[AllowAnonymous]` is forbidden at the controller-class level by convention; only method-level. Architecture test:

```csharp
[Fact]
public void Controllers_must_not_have_class_level_AllowAnonymous()
{
    var violations = typeof(Program).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
        .Where(t => t.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
        .Select(t => t.Name)
        .ToList();

    violations.Should().BeEmpty();
}
```

A second architecture test ensures every controller class (or every action method) has either `[Authorize]` or `[AllowAnonymous]` declared explicitly — catches the "I forgot to think about auth on this one" case:

```csharp
[Fact]
public void Every_controller_action_must_declare_authorization_intent()
{
    var violations = typeof(Program).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
        .Where(m => m.GetCustomAttribute<AuthorizeAttribute>() is null
                 && m.GetCustomAttribute<AllowAnonymousAttribute>() is null
                 && m.DeclaringType!.GetCustomAttribute<AuthorizeAttribute>() is null)
        .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
        .ToList();

    violations.Should().BeEmpty(because: "explicit auth attribute required on every action");
}
```

### 7. ICurrentUserAccessor swap

`HttpContextCurrentUserAccessor : ICurrentUserAccessor`:

```csharp
public sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _http;
    public HttpContextCurrentUserAccessor(IHttpContextAccessor http) => _http = http;

    public Guid UserId
    {
        get
        {
            var claim = _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(claim, out var id))
                throw new UnauthorizedAccessException("Current user is not authenticated.");
            return id;
        }
    }
}
```

DI registration in 6a is unconditional:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
```

`SingleUserAccessor` is **kept in the codebase** because Stage 7's data remap migration references the sentinel `Guid` constant — but it is no longer registered in DI. Stage 7 deletes the class entirely after the migration runs.

**Local dev impact:** anyone running `dotnet run` and hitting an authenticated endpoint gets 401 until they register and log in via the new `/api/auth/register` + `/api/auth/login` endpoints. This is intentional. Stage 6b/6c add TOTP and email verification on top; until then, 6a's endpoints accept email + password directly.

### 8. Auth endpoints (bare-minimum, JSON only)

Three new endpoints in a new `AuthController`:

#### `POST /api/auth/register`

```jsonc
// Request
{ "email": "user@example.com", "password": "...", "rememberMe": false }
```

- `[AllowAnonymous]`, `[ValidateAntiForgeryToken]` (via global filter).
- Validates email format, runs HIBP check, runs all `IPasswordValidator<>`s.
- Calls `UserManager.CreateAsync(user, password)` — Identity uses the registered `Argon2idPasswordHasher`.
- Returns `204 No Content` on success. Returns `400` with `{ errors: [...] }` shape on validation failure (matches `api-contract.md` envelope).
- **Does not auto-sign-in.** This is the Microsoft-canonical flow when `RequireConfirmedAccount/Email = true` — see "Production registration flow" below for full sequence and rationale.

##### Production registration flow (canonical, applies once Stage 6c ships)

The Microsoft Learn documentation for ASP.NET Core 10 is explicit on this:

> *"To require a confirmed account and prevent immediate login at registration, set `DisplayConfirmAccountLink = false`... When a user registers, if `_userManager.Options.SignIn.RequireConfirmedAccount` is true, the user is redirected to a `RegisterConfirmation` page instead of being automatically logged in."*
> — [Account confirmation and password recovery in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/accconfirm?view=aspnetcore-10.0)

The full production flow has **four** steps:

1. `POST /api/auth/register` → `UserManager.CreateAsync(user, password)` succeeds. `EmailConfirmed = false`. Server generates a token via `GenerateEmailConfirmationTokenAsync` and sends a confirmation email. Response: `204 No Content`. **No session cookie issued.** *(Token generation + email send ship in Stage 6c.)*
2. User clicks the link → `GET /api/auth/confirm-email?userId={id}&token={token}` → server calls `UserManager.ConfirmEmailAsync(user, token)`, flips `EmailConfirmed = true`. *(Endpoint ships in Stage 6c.)*
3. User goes to the login page → `POST /api/auth/login` → `SignInManager.PasswordSignInAsync` succeeds (now that `EmailConfirmed = true`).
4. The `UserSession` row is inserted on this first successful login, not on register.

##### Why no auto-sign-in?

Auto-sign-in **before** email confirmation defeats the entire point of email confirmation. The confirmation step exists to prove the registrant controls the email address. If registration auto-issues a session, an attacker who registers `victim@example.com` (a typo or deliberate impersonation) gets a working session before the real victim ever sees a confirmation email — even if the session is short-lived, that's a real attack window. With `SignIn.RequireConfirmedEmail = true`, `SignInManager.PasswordSignInAsync` returns `SignInResult.NotAllowed` until `EmailConfirmed = true`, blocking that path.

The "register and you're logged in" pattern that some apps ship is what happens when:
- The app sets `RequireConfirmedAccount/Email = false` (lower bar, more friction-free onboarding).
- The app uses passwordless magic-link sign-up, where registration *is* the confirmation step.
- The app issues a session immediately and *defers* email verification ("verify within 7 days or your account is locked"). This is a deliberate UX trade-off — `security-model.md` explicitly does not take this path.

Project Ceres takes the secure path: confirm-first, then sign-in. The local-dev gap during Stage 6a is a known consequence of merging the two halves of the auth feature in a single stage but shipping them in two — it is closed when 6c ships the email-send + `ConfirmEmail` handler.

**6a transitional rule:** `SignIn.RequireConfirmedEmail = true` is set globally, which means logins fail until email verification ships in 6c. To allow integration tests to exercise the login pipeline in 6a, the test fixture's `RegisterUserAsync(email, password)` helper calls `UserManager.SetEmailConfirmedAsync(user, true)` immediately after creation — bypassing the verification flow per-user, without toggling the global option. **Real local dev cannot log in until 6c.** This is documented in the spec acceptance criteria below; nothing user-facing relies on this gap.

> **Test-fixture shortcut tracked for 6c removal.** `RegisterUserAsync` bypasses the production-canonical email-confirmation flow because in 6a there is no `IEmailSender` and no `ConfirmEmail` handler to drive. When Stage 6c ships those, the fixture must be revisited:
> - Either: replace `SetEmailConfirmedAsync` with a real test path that drives the registration → token-generation → confirmation handler flow (closer to production, more brittle, more code).
> - Or: keep `SetEmailConfirmedAsync` as the *fixture* path (registers a confirmed user fast for unrelated tests) **and** add a separate test class that exercises the full register → confirm → login pipeline end-to-end through the actual endpoints.
>
> The latter is the recommended path — splits "I need a confirmed user" (most tests) from "the email-confirmation pipeline works" (one focused test class). Tracked as a Stage 6c spec input.

#### `POST /api/auth/login`

```jsonc
// Request
{ "email": "user@example.com", "password": "...", "rememberMe": false }
```

- `[AllowAnonymous]`, `[ValidateAntiForgeryToken]`.
- Lookup user by email. **If not found: still call `Argon2idPasswordHasher.RunDummyHash()`** (constant-time enumeration prevention), return `401` with a generic error. **Same response shape, same HTTP status, same wall-clock timing as a wrong-password failure.**
- If found: call `SignInManager.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true)`.
  - Identity invokes `Argon2idPasswordHasher.VerifyHashedPassword`. On `SuccessRehashNeeded`, Identity automatically re-hashes and persists.
- On success:
  1. Generate a new `Guid SessionId`.
  2. Capture `clientIp` and `userAgent` from `HttpContext`.
  3. If `rememberMe`: generate a 256-bit base64url persistent token, hash it, set `__Host-Persist` cookie.
  4. Insert `UserSession` row with these values (passed via `HttpContext.Items` to `ApplicationUserClaimsPrincipalFactory`).
  5. The principal factory adds the `"sid"` claim. Identity sets `__Host-Session`.
  6. Call `IAntiforgery.GetAndStoreTokens(HttpContext)` to rotate the CSRF cookie.
- Returns `204 No Content` on success.

#### `POST /api/auth/logout`

- Default fallback policy applies — requires auth.
- `[ValidateAntiForgeryToken]` enforced via global filter.
- Read `"sid"` claim, set `UserSession.RevokedAt = DateTime.UtcNow`.
- If `__Host-Persist` cookie present, parse it, find the matching `UserSession` by `PersistentTokenHash`, set `RevokedAt` on that row too. Clear `__Host-Persist`.
- Call `SignOutAsync()` to clear `__Host-Session`.
- Call `IAntiforgery.GetAndStoreTokens(HttpContext)` to rotate the CSRF cookie.
- Returns `204 No Content`.

**Out of scope for 6a (these belong to 6b/6c):**
- TOTP step in login flow.
- Lockout self-service unlock email.
- Failed-login logging.
- Rate limiting on `/login` and `/register`.
- Password reset.
- Email verification + email change.
- Re-authentication middleware for sensitive operations.
- Audit log writes.

---

## Migrations

Two new EF migrations ship in 6a:

1. `AddIdentitySchema` — Identity tables only.
2. `AddUserSessionAndBlockedIp` — `UserSession`, `UserBlockedIp` plus indexes.

Both run cleanly against the existing dev database (which already has the sentinel `Guid` populated on `Movement`, `Budget`, `Account`, etc.). No data migration in 6a — Stage 7 handles the sentinel-to-real-user remap.

---

## Configuration

New `appsettings.json` keys:

```jsonc
{
  "Authentication": {
    "Argon2id": {
      "MemorySizeKb": 19456,
      "Iterations": 2,
      "Parallelism": 1
    },
    "Cookie": {
      "SessionExpiryMinutes": 30,
      "PersistentExpiryDays": 30
    },
    "IpEnforcement": {
      "DefaultEnabled": false
    }
  }
}
```

Argon2id values are read into a strongly-typed options class (`Argon2idOptions`) but cross-checked against the const-pinned values in the hasher; mismatch fails fast at startup. The config key exists for future cryptographic agility (raising parameters when hardware allows) but is not user-tunable in 6a.

`__Host-` cookies require HTTPS. Local dev must run `dotnet run` with the HTTPS profile (already the default in `launchSettings.json`). Verified once during 6a — no changes needed.

---

## Testing strategy

All tests are xUnit + `WebApplicationFactory<Program>` integration tests in a new `ProjectCeres.Tests/Authentication/` folder. **No Playwright in 6a** — that lands in Stage 9.

Test database: PostgreSQL via Testcontainers (existing pattern in the repo). Each test class gets a fresh database; per-test data is created via the test fixture's `RegisterUserAsync(email, password)` helper.

### Required passing tests before 6a is considered complete

**Argon2id:**
- Hash + verify round-trip succeeds.
- Tampered hash returns `Failed`.
- Hash with old parameters (e.g., `m=4096`) returns `SuccessRehashNeeded` on verify; subsequent `UserManager.ChangePasswordAsync` (or any login flow that triggers re-hash) updates the stored hash to the current params.
- `RunDummyHash()` runs in approximately the same wall-clock time as a real hash (within ±20% over 100 runs).

**Identity wiring:**
- `RegisterUserAsync` creates an `AspNetUsers` row.
- Password is stored as a PHC string starting with `$argon2id$v=19$m=19456,t=2,p=1$`.
- Email uniqueness enforced (duplicate registration returns 400).

**Sessions:**
- Successful login inserts a `UserSession` row with correct `UserId`, `IpCreatedAt`, `UserAgent`, `IsPersistent = false`.
- Login with `rememberMe=true` issues `__Host-Persist` cookie and inserts a row with `PersistentTokenHash != null`.
- Subsequent authenticated request with valid `__Host-Session` passes; `LastUsedAt` is updated.
- Logout sets `RevokedAt`; the next request with the same cookie returns 401.
- Anonymous request to `/api/transactions` (or any authenticated endpoint) returns 401 — fallback policy enforced.

**Persistent token rotation:**
- Request arriving with no `__Host-Session` but valid `__Host-Persist` succeeds, issues a new `__Host-Persist` cookie, invalidates the old `PersistentTokenHash`, inserts a new `UserSession` row.
- The old `__Host-Persist` cookie value, replayed after rotation, returns 401.

**IP block:**
- Adding a `UserBlockedIp` row for a user → that user's request from the blocked IP returns 403.
- Adding a `UserBlockedIp` row revokes all `UserSession` rows for that user with matching `IpCreatedAt` (verified by row state in DB).

**CSRF:**
- `POST /api/auth/login` without `X-XSRF-TOKEN` header returns 400.
- `POST /api/auth/login` with valid cookie + matching header succeeds.
- `__Host-XSRF` cookie is rotated on login (cookie value before login ≠ value after).
- `__Host-XSRF` cookie is rotated on logout.

**Cookie attributes:**
- After login, `Set-Cookie: __Host-Session` includes `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, and **no** `Domain` attribute.
- After login with `rememberMe=true`, `Set-Cookie: __Host-Persist` includes the same set.
- `__Host-XSRF` includes `Secure`, `SameSite=Lax`, `Path=/`, **no** `HttpOnly`, **no** `Domain`.

**Architecture tests:**
- No controller has class-level `[AllowAnonymous]`.
- Every controller action declares either `[Authorize]` or `[AllowAnonymous]` (directly or via a class-level `[Authorize]`).
- No `[HttpGet]` action name starts with a write verb (`Create`, `Update`, `Delete`, `Archive`, `Restore`, `Reset`, `Send`, `Approve`, `Reject`).

**Password policy:**
- `register` rejects passwords < 8 chars.
- `register` rejects passwords < 15 chars when MFA is not enrolled (which is always the case in 6a).
- `register` accepts passwords ≥ 15 chars without uppercase/digit/symbol (no composition rules).
- HIBP-screened password (e.g. `Password1!`, `qwertyuiop`) is rejected — uses a local fixture HIBP service in tests, not a network call.

### Tests intentionally NOT in 6a

These belong to 6b/6c and must not be scoped into 6a:

- TOTP enrollment, verification, replay prevention, backup codes.
- Account lockout after 10 failed attempts (option set in 6a, but the failed-login *logging* table and lockout email arrive in 6b).
- Rate limiting (`/login`, `/register`, `/password-reset`).
- Password reset flow.
- Email verification + email-change flow.
- Re-authentication middleware tests.
- Audit log writes.

---

## Verification checklist (Stage 6a slice of roadmap)

From `docs/roadmap-phase-three.md` § Stage 6 verification, the following items are covered by 6a:

ASP.NET Identity hardening:
- [x] `options.Lockout.MaxFailedAccessAttempts = 10`
- [x] `options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15)`
- [x] `options.Lockout.AllowedForNewUsers = true`
- [x] `options.User.RequireUniqueEmail = true`
- [x] `options.SignIn.RequireConfirmedEmail = true`
- [x] `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.FromMinutes(5)`

Password handling:
- [x] Default `IPasswordHasher<TUser>` replaced with Argon2id pinned to `m=19456, t=2, p=1`
- [x] Password policy: minimum 8 characters, no maximum below 64
- [x] No mandatory complexity rules; HIBP screening applied
- [x] Login endpoint always runs Argon2id hash (dummy on user-not-found)
- [ ] Password reset endpoints always run Argon2id hash — **deferred to 6c**

`UserSession` table + token rotation:
- [x] `UserSession` entity with all spec fields
- [x] Session token regenerated immediately after login (Identity ticket re-issued by `SignInAsync`)
- [x] Logout marks `RevokedAt`; cookie cleared; subsequent request rejected
- [x] Persistent ("remember me") sessions: rotated on each use
- [x] Per-user IP block list (`UserBlockedIp`) revokes all sessions from that IP on add
- [ ] Per-session IP enforcement honoured (toggle wired but defaults off) — **toggle UI deferred to 6c**

CSRF:
- [x] `IAntiforgery` middleware registered globally
- [x] All state-changing endpoints validate the XSRF-TOKEN
- [x] `XSRF-TOKEN` cookie issued: `Secure=true`, `SameSite=Lax`, `HttpOnly=false`
- [x] CSRF token rotation on login/logout
- [x] Login POST validates antiforgery (`[AllowAnonymous]` does not exempt CSRF)

Global authorization:
- [x] `AddAuthorization` configured with `FallbackPolicy = RequireAuthenticatedUser()`
- [x] `[AllowAnonymous]` whitelist enforced (4 endpoints in 6a; full list extends in 6c)
- [x] Architecture test: any controller without `[Authorize]` or `[AllowAnonymous]` fails the build

Cookie configuration:
- [x] Auth cookie name uses `__Host-` prefix
- [x] `HttpOnly = true`, `Secure = true`, `SameSite = Lax`
- [x] No `Domain` attribute
- [x] `Path = /`
- [x] Verified by integration test (DevTools verification deferred until login UI ships in Stage 9)

**Items deferred to 6b:** rate limiting, failed-login logging, lockout self-service unlock email, TOTP infrastructure.

**Items deferred to 6c:** password reset, email verification, email-change, re-authentication middleware, audit log.

---

## Open questions

None — all design decisions resolved in brainstorming on 2026-05-09.

---

## References

- `docs/roadmap-phase-three.md` § Stage 6 — Identity infrastructure (Batch 3b)
- `docs/security-model.md` § Passwords, § Sessions, § CSRF, § Cookie Configuration, § Global Authorization Policy, § ASP.NET Core Identity Hardening
- `docs/planning-phase3.md` § Password policy, § MFA
- `docs/decisions/ADR-0019-session-management-user-configurable-with-ip-controls.md`
- `docs/decisions/ADR-0063-cookie-samesite-lax-with-csrf-tokens.md`
- `docs/decisions/ADR-0066-sentinel-remap-to-first-registered-user.md`
- NIST SP 800-63B-4 (final 2025-07-31): https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-63B-4.pdf
- OWASP Authentication Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html
- OWASP Password Storage Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html
