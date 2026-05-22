# Stage 9.3 — `/register` + email verification — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the `/register` SPA page + the `/email-verify` interstitial + the full server-side email-confirmation token pipeline so a fresh registration flows end-to-end: form → server token row + email send → email link → token consume → email verified → login succeeds.

**Architecture:** Mirror `PasswordReset` plumbing (entity + generator + service + controller + resx) for the backend half; mirror `PasswordReset.tsx` / `LoginTotp.tsx` for the SPA half. No new abstractions — concrete duplication per the project's anti-abstraction rule. Anti-enumeration on duplicate-email and unconfirmed-email paths via Argon2id cost mirroring.

**Tech Stack:** ASP.NET Core MVC + EF Core (Npgsql) + ASP.NET Identity + Argon2id; React 19 + Vite + Vitest + react-hook-form + zod + react-i18next + shadcn (base-nova).

**Spec:** `docs/superpowers/specs/2026-05-18-stage-9-3-register-and-email-verify-design.md` (revised 2026-05-22).

---

## File structure

**New backend files:**
- `ProjectCeres/Models/EmailConfirmationToken.cs` — entity
- `ProjectCeres/Common/Authentication/EmailConfirmationTokenGenerator.cs` — Generate / Hash / Verify
- `ProjectCeres/Common/Authentication/EmailConfirmationService.cs` — IssueAsync / RequestResendAsync / ConfirmAsync
- `ProjectCeres/Common/Authentication/EmailConfirmationConfirmOutcome.cs` — Success / InvalidToken records
- `ProjectCeres/Controllers/Api/EmailVerificationController.cs` — two actions
- `ProjectCeres/ViewModels/Auth/EmailVerifyRequest.cs` — `{Token}`
- `ProjectCeres/ViewModels/Auth/EmailVerifyResendRequest.cs` — `{Email}`
- `ProjectCeres/Migrations/<timestamp>_AddEmailConfirmationTokens.cs`

**Modified backend files:**
- `ProjectCeres/Controllers/Api/AuthController.cs` — Register handler (fresh + duplicate branches), Login handler (IsNotAllowed branch)
- `ProjectCeres/Data/AppDbContext.cs` — DbSet + OnModelCreating
- `ProjectCeres/Common/Email/EmailTemplateKey.cs` — add `RegistrationConfirmation`
- `ProjectCeres/Resources/EmailsResource.en.resx` + `.es.resx` — add 3 keys × 2 languages
- `ProjectCeres/Program.cs` — DI registrations
- `ProjectCeres/Common/Authentication/FailedLoginReason.cs` — add `EmailNotConfirmed`
- `ProjectCeres/Models/AuditLog.cs` — add `EmailVerificationRequested` + `EmailVerified` enum values

**New SPA files:**
- `ProjectCeres.Client/src/app/pages/auth/Register.tsx`
- `ProjectCeres.Client/src/app/pages/auth/Register.test.tsx`
- `ProjectCeres.Client/src/app/pages/auth/EmailVerify.tsx`
- `ProjectCeres.Client/src/app/pages/auth/EmailVerify.test.tsx`
- `ProjectCeres.Client/src/app/auth/schemas/register.schema.ts`

**Modified SPA files:**
- `ProjectCeres.Client/src/app/App.tsx` — replace `RegisterPlaceholder` route, add `/email-verify` route
- `ProjectCeres.Client/src/app/pages/auth/Login.tsx` — fix `onResendVerification` to send `{email}`
- `ProjectCeres.Client/src/i18n/locales/en.json` + `es.json` — add `auth.register.*` + `auth.emailVerify.*`
- (Delete) `ProjectCeres.Client/src/app/pages/auth/RegisterPlaceholder.tsx`

**New test files:**
- `ProjectCeres.Tests/Integration/EmailConfirmationTests.cs` — 14 [Fact]s per spec § Tests

---

## Task 1: `EmailConfirmationToken` entity + DbContext binding

**Files:**
- Create: `ProjectCeres/Models/EmailConfirmationToken.cs`
- Modify: `ProjectCeres/Data/AppDbContext.cs`

- [ ] **Step 1: Write the entity**

```csharp
// ProjectCeres/Models/EmailConfirmationToken.cs
using ProjectCeres.Common;

namespace ProjectCeres.Models;

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

- [ ] **Step 2: Add DbSet + OnModelCreating block to `AppDbContext`**

Locate the `PasswordResetTokens` DbSet declaration and add `EmailConfirmationTokens` immediately after it (same alphabetical ordering pattern). In `OnModelCreating`, mirror the `PasswordResetToken` configuration block:

```csharp
modelBuilder.Entity<EmailConfirmationToken>(b =>
{
    b.HasKey(e => e.Id);
    b.Property(e => e.TokenLookup).IsRequired();
    b.Property(e => e.TokenHash).IsRequired();
    b.HasIndex(e => e.TokenLookup).IsUnique();
    b.HasIndex(e => e.UserId);
    b.ToTable("EmailConfirmationTokens");
});
```

Use existing user_isolation RLS conventions (no FK to AspNetUsers per Stage 6.10 pattern — Stage 7 adds FKs). Apply the user-owned EF global filter the same way `PasswordResetToken` is configured.

- [ ] **Step 3: Compile to verify**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Models/EmailConfirmationToken.cs ProjectCeres/Data/AppDbContext.cs
git commit -m "feat(9.3): add EmailConfirmationToken entity + DbContext binding"
```

---

## Task 2: EF migration

**Files:**
- Create: `ProjectCeres/Migrations/<timestamp>_AddEmailConfirmationTokens.cs`

- [ ] **Step 1: Generate the migration**

```bash
dotnet ef migrations add AddEmailConfirmationTokens --project ProjectCeres --output-dir Migrations
```

- [ ] **Step 2: Inspect the generated file**

Expected `Up()` body:

```csharp
migrationBuilder.CreateTable(
    name: "EmailConfirmationTokens",
    columns: table => new
    {
        Id = table.Column<Guid>(type: "uuid", nullable: false),
        UserId = table.Column<Guid>(type: "uuid", nullable: false),
        TokenLookup = table.Column<byte[]>(type: "bytea", nullable: false),
        TokenHash = table.Column<string>(type: "text", nullable: false),
        CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
        ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
        ConsumedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
    },
    constraints: table => table.PrimaryKey("PK_EmailConfirmationTokens", x => x.Id));

migrationBuilder.CreateIndex(
    name: "IX_EmailConfirmationTokens_TokenLookup",
    table: "EmailConfirmationTokens",
    column: "TokenLookup",
    unique: true);

migrationBuilder.CreateIndex(
    name: "IX_EmailConfirmationTokens_UserId",
    table: "EmailConfirmationTokens",
    column: "UserId");
```

If the column types are `timestamp with time zone` instead of `timestamp without time zone`, mirror whatever `PasswordResetTokens` migration used (consistency within the codebase wins).

- [ ] **Step 3: Apply the migration**

```bash
dotnet ef database update --project ProjectCeres
```

Expected: "Applying migration '<timestamp>_AddEmailConfirmationTokens'. Done."

- [ ] **Step 4: Verify the table exists**

```bash
psql project_ceres -c "\d \"EmailConfirmationTokens\""
```

Expected: table with the 7 columns + two indexes.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Migrations/
git commit -m "feat(9.3): EF migration adding EmailConfirmationTokens table"
```

---

## Task 3: `EmailConfirmationTokenGenerator`

**Files:**
- Create: `ProjectCeres/Common/Authentication/EmailConfirmationTokenGenerator.cs`

- [ ] **Step 1: Mirror `PasswordResetTokenGenerator`**

Copy `ProjectCeres/Common/Authentication/PasswordResetTokenGenerator.cs` to `EmailConfirmationTokenGenerator.cs` and rename the class. No behavioural change — same 32-byte random token, same Argon2id PHC hashing, same Verify shape.

- [ ] **Step 2: Compile**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj
```

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Common/Authentication/EmailConfirmationTokenGenerator.cs
git commit -m "feat(9.3): EmailConfirmationTokenGenerator (sibling of PasswordResetTokenGenerator)"
```

---

## Task 4: `EmailConfirmationConfirmOutcome` discriminated union

**Files:**
- Create: `ProjectCeres/Common/Authentication/EmailConfirmationConfirmOutcome.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace ProjectCeres.Common.Authentication;

public abstract record EmailConfirmationConfirmOutcome
{
    public sealed record Success(Guid UserId) : EmailConfirmationConfirmOutcome;
    public sealed record InvalidToken : EmailConfirmationConfirmOutcome;
}
```

- [ ] **Step 2: Commit (will compile after Task 5)**

```bash
git add ProjectCeres/Common/Authentication/EmailConfirmationConfirmOutcome.cs
git commit -m "feat(9.3): EmailConfirmationConfirmOutcome union"
```

---

## Task 5: `EmailConfirmationService` (write the failing tests first)

**Files:**
- Create: `ProjectCeres.Tests/Integration/EmailConfirmationTests.cs`
- Create: `ProjectCeres/Common/Authentication/EmailConfirmationService.cs`

- [ ] **Step 1: Write the 14-test integration file (RED)**

Skeleton — fill each `[Fact]` body to match the spec § Tests list. Use the existing `WebApplicationFactoryFixture` pattern from `PasswordResetTests.cs` for setup. Each test mints a fresh user, exercises the service, asserts the postcondition.

Key snippets:

```csharp
// Test 1: Register issues a token
[Fact]
public async Task Register_writes_token_row_with_TokenLookup_and_30min_expiry()
{
    var email = $"reg-{Guid.NewGuid():N}@test.local";
    var resp = await _client.PostAsJsonAsync("/api/auth/register",
        new { email, password = "ValidPass!2026" });
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var userId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
        .FindByEmailAsync(email))!.Id;
    var row = await db.EmailConfirmationTokens.IgnoreQueryFilters()
        .Where(t => t.UserId == userId).FirstAsync();

    row.TokenLookup.Should().NotBeEmpty();
    row.TokenHash.Should().NotBeNullOrEmpty();
    row.ConsumedAt.Should().BeNull();
    (row.ExpiresAt - row.CreatedAt).Should().BeCloseTo(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(2));
}

// Test 9: Resend with confirmed-email is a no-op
[Fact]
public async Task Resend_for_confirmed_email_does_NOT_write_new_token_row()
{
    var email = $"conf-{Guid.NewGuid():N}@test.local";
    await RegisterAndManuallyConfirmAsync(email);  // helper: register + flip EmailConfirmed=true

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var beforeCount = await db.EmailConfirmationTokens.IgnoreQueryFilters().CountAsync();

    var resp = await _client.PostAsJsonAsync("/api/auth/email/verify/resend", new { email });
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var afterCount = await db.EmailConfirmationTokens.IgnoreQueryFilters().CountAsync();
    afterCount.Should().Be(beforeCount);
}

// Test 13: Login for unconfirmed user returns EMAIL_NOT_CONFIRMED
[Fact]
public async Task Login_for_unconfirmed_user_returns_EMAIL_NOT_CONFIRMED()
{
    var email = $"unc-{Guid.NewGuid():N}@test.local";
    var password = "ValidPass!2026";
    await _client.PostAsJsonAsync("/api/auth/register", new { email, password });

    var resp = await _client.PostAsJsonAsync("/api/auth/login",
        new { email, password, rememberMe = false });
    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    var payload = await resp.Content.ReadFromJsonAsync<ErrorEnvelope>();
    payload!.Error.Code.Should().Be("EMAIL_NOT_CONFIRMED");
}
```

- [ ] **Step 2: Run the tests — expect all to fail (RED)**

```bash
dotnet test --filter "FullyQualifiedName~EmailConfirmation" --no-restore
```

Expected: 14 failures (most with "no such endpoint" / "no DbSet").

- [ ] **Step 3: Implement `EmailConfirmationService`**

Copy `PasswordResetService.cs` as the starting template; strip MFA, password-policy, and supersede-on-confirm branches. Public surface:

```csharp
public sealed class EmailConfirmationService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
    public const int EmailRateLimit = 5;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    public sealed class RateLimitedException : Exception
    {
        public int RetryAfterSeconds { get; }
        public RateLimitedException(int retryAfterSeconds)
            : base("Email-verification rate limit exceeded.")
            => RetryAfterSeconds = retryAfterSeconds;
    }

    public async Task IssueAsync(Guid userId, string email, string verifyUrlBase, CancellationToken ct);
    public async Task RequestResendAsync(string email, CancellationToken ct);
    public async Task<EmailConfirmationConfirmOutcome> ConfirmAsync(string rawToken, CancellationToken ct);
}
```

`IssueAsync` implementation (sketch):
- Per-user semaphore.
- Open `_db.BeginPreAuthUserScopeAsync(userId, ct)`.
- Bulk-supersede `Where(t => t.UserId == userId && t.ConsumedAt == null)` → `SetProperty(t => t.ConsumedAt, DateTime.UtcNow)`.
- Generate raw token + hash + lookup.
- Insert new row.
- `SaveChangesAsync`. `scope.CommitAsync`.
- Outside the lock: build URL `${verifyUrlBase.TrimEnd('/')}/email-verify#token=${rawToken}`, compose via `_composer.Compose(EmailTemplateKey.RegistrationConfirmation, culture, verifyUrl) with { To = recipient }`, send, swallow exceptions with `_logger.LogError`.
- Audit-write `AuditLogAction.EmailVerificationRequested`.

`RequestResendAsync` implementation (sketch):
- Trim + normalize email.
- Rate-limit gate (mirror `PasswordResetService.EnforceEmailRateLimit`).
- `_argon.RunDummyHash()` once (matches FindByEmail cost).
- `FindByEmailAsync(normalized)`:
  - If null → `_argon.RunDummyHash()` once more (mirror the issue-cost) → return.
  - If user.EmailConfirmed → `_argon.RunDummyHash()` once more → return.
  - Else → call `IssueAsync(user.Id, user.Email!, verifyUrlBase, ct)`.

`ConfirmAsync` implementation (sketch):
- Empty/whitespace → dummy hash → InvalidToken.
- Lookup via `_admin.EmailConfirmationTokens.IgnoreQueryFilters()` by `TokenLookup` (AdminDbContext pattern from PasswordResetService.ConfirmAsync).
- If match is null → dummy hash → InvalidToken.
- Verify TokenHash; on mismatch → InvalidToken.
- Per-user semaphore. Inside `BeginPreAuthUserScopeAsync(match.UserId, ct)`:
  - Re-read by Id; if consumed/expired between match and lock → InvalidToken.
  - `_userManager.FindByIdAsync(match.UserId.ToString())`; null → InvalidToken.
  - `user.EmailConfirmed = true; await _userManager.UpdateAsync(user);`
  - `ExecuteUpdateAsync` to set `ConsumedAt`.
  - Audit-write `AuditLogAction.EmailVerified`.
  - `scope.CommitAsync`.
  - Return `new Success(user.Id)`.

- [ ] **Step 4: Add `EmailVerificationRequested` + `EmailVerified` to `AuditLogAction`**

```csharp
// ProjectCeres/Models/AuditLog.cs
public enum AuditLogAction
{
    LoginSucceeded,
    // ... existing values ...
    EmailVerificationRequested,
    EmailVerified,
}
```

- [ ] **Step 5: DI registration**

In `ProjectCeres/Program.cs`, after `builder.Services.AddScoped<PasswordResetTokenGenerator>();` (line ~150):

```csharp
builder.Services.AddScoped<EmailConfirmationTokenGenerator>();
builder.Services.AddScoped<EmailConfirmationService>();
```

- [ ] **Step 6: Don't run tests yet — Task 6 wires the controller. The 14 tests still fail at this point because no HTTP route exists. Commit the service as a unit:**

```bash
git add ProjectCeres/Common/Authentication/EmailConfirmationService.cs \
        ProjectCeres/Models/AuditLog.cs \
        ProjectCeres/Program.cs
git commit -m "feat(9.3): EmailConfirmationService + AuditLog enum values + DI"
```

---

## Task 6: HTTP endpoints (controller + DTOs + Register wiring)

**Files:**
- Create: `ProjectCeres/Controllers/Api/EmailVerificationController.cs`
- Create: `ProjectCeres/ViewModels/Auth/EmailVerifyRequest.cs`
- Create: `ProjectCeres/ViewModels/Auth/EmailVerifyResendRequest.cs`
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`

- [ ] **Step 1: DTOs**

```csharp
// ProjectCeres/ViewModels/Auth/EmailVerifyRequest.cs
using System.ComponentModel.DataAnnotations;
namespace ProjectCeres.ViewModels.Auth;
public sealed class EmailVerifyRequest
{
    [Required, StringLength(128)]
    public string Token { get; set; } = "";
}
```

```csharp
// ProjectCeres/ViewModels/Auth/EmailVerifyResendRequest.cs
using System.ComponentModel.DataAnnotations;
namespace ProjectCeres.ViewModels.Auth;
public sealed class EmailVerifyResendRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";
}
```

- [ ] **Step 2: Controller**

```csharp
// ProjectCeres/Controllers/Api/EmailVerificationController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Authorization;
using ProjectCeres.Common.RateLimiting;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/email")]
public sealed class EmailVerificationController : ControllerBase
{
    private readonly EmailConfirmationService _service;
    public EmailVerificationController(EmailConfirmationService service) => _service = service;

    [HttpPost("verify"), AllowAnonymous, PreAuthCallSite("Email.Verify")]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Verify([FromBody] EmailVerifyRequest request)
    {
        var outcome = await _service.ConfirmAsync(request.Token, HttpContext.RequestAborted);
        return outcome switch
        {
            EmailConfirmationConfirmOutcome.Success => NoContent(),
            EmailConfirmationConfirmOutcome.InvalidToken =>
                Unauthorized(new { error = new { code = "INVALID_VERIFICATION_TOKEN",
                                                 message = "The verification link is invalid or expired." } }),
            _ => throw new InvalidOperationException($"Unhandled {outcome.GetType().Name}"),
        };
    }

    [HttpPost("verify/resend"), AllowAnonymous, PreAuthCallSite("Email.VerifyResend")]
    [EnableRateLimiting(AuthRateLimitPolicies.EmailByUser)]
    [ApplyEmailIpRateLimit]
    public async Task<IActionResult> RequestResend([FromBody] EmailVerifyResendRequest request)
    {
        try
        {
            await _service.RequestResendAsync(request.Email, HttpContext.RequestAborted);
            return NoContent();
        }
        catch (EmailConfirmationService.RateLimitedException ex)
        {
            Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString();
            return StatusCode(429, new { error = new { code = "RATE_LIMITED",
                                                       message = "Too many requests." } });
        }
    }
}
```

- [ ] **Step 3: Wire Register handler — three-branch shape**

In `ProjectCeres/Controllers/Api/AuthController.cs:75-113`, replace the existing body with:

```csharp
[HttpPost("register"), AllowAnonymous, PreAuthCallSite("Auth.Register")]
[EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
public async Task<IActionResult> Register([FromBody] RegisterRequest request)
{
    if (!ModelState.IsValid) return ValidationProblem(ModelState);

    await using var tx = await _db.Database.BeginTransactionAsync(HttpContext.RequestAborted);

    var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
    var result = await _userManager.CreateAsync(user, request.Password);

    var verifyUrlBase = $"{Request.Scheme}://{Request.Host}";

    if (!result.Succeeded)
    {
        var isDuplicateOnly = result.Errors.All(e =>
            e.Code == "DuplicateUserName" || e.Code == "DuplicateEmail");
        if (isDuplicateOnly && result.Errors.Any())
        {
            // Three-branch anti-enumeration:
            //  - Confirmed-existing email: dummy Argon2id to mirror token-write cost; no email sent.
            //  - Unconfirmed-existing email: issue a fresh token; same code path as fresh-create.
            //  - Fresh email: handled in the !isDuplicateOnly path below (impossible here).
            var existing = await _userManager.FindByEmailAsync(request.Email);
            if (existing is not null)
            {
                if (existing.EmailConfirmed)
                {
                    _argon.RunDummyHash();
                }
                else
                {
                    await _emailConfirmation.IssueAsync(
                        existing.Id, existing.Email!, verifyUrlBase, HttpContext.RequestAborted);
                }
            }
            await tx.CommitAsync(HttpContext.RequestAborted);  // empty tx, no-op
            return NoContent();
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Code, error.Description);
        }
        return ValidationProblem(ModelState);
    }
    await _categorySeedService.CopyDefaultsForUserAsync(user.Id, HttpContext.RequestAborted);
    await _auditLog.RecordAsync(user.Id, AuditLogAction.Registered, ct: HttpContext.RequestAborted);
    await tx.CommitAsync(HttpContext.RequestAborted);

    // Fresh-create: issue email-verification token AFTER the tx commits so a
    // send failure doesn't roll back user creation. Send failures are swallowed
    // inside IssueAsync per the PasswordResetService.RequestAsync pattern.
    await _emailConfirmation.IssueAsync(user.Id, user.Email!, verifyUrlBase, HttpContext.RequestAborted);

    return NoContent();
}
```

Constructor changes: add `EmailConfirmationService _emailConfirmation` as a dep + assignment in the constructor.

- [ ] **Step 4: Wire Login IsNotAllowed branch**

In `ProjectCeres/Controllers/Api/AuthController.cs`, in the Login action, insert the IsNotAllowed branch **before** the `if (signIn.IsLockedOut)` check (~line 195):

```csharp
if (signIn.IsNotAllowed)
{
    HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
    var (ip, ua) = RequestContext();
    await _failedLogins.RecordAsync(
        request.Email, userStub.Id, FailedLoginReason.EmailNotConfirmed, ip, ua, HttpContext.RequestAborted);
    return UnauthorizedEnvelope("EMAIL_NOT_CONFIRMED",
        "Verify your email before signing in. Check your inbox or request a new link.");
}
```

Add `EmailNotConfirmed` to `ProjectCeres/Common/Authentication/FailedLoginReason.cs`.

- [ ] **Step 5: Run the 14 EmailConfirmation tests — most should now pass**

```bash
dotnet test --filter "FullyQualifiedName~EmailConfirmation" --no-restore
```

Expected: 14/14 passing (or failing with clear assertion mismatches I should iterate on).

- [ ] **Step 6: Run full backend suite for regression check**

```bash
dotnet test --filter "FullyQualifiedName~ProjectCeres.Tests"
```

Expected: still ~1098 passing (1098 baseline + 14 new = 1112). Investigate any new failures.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Controllers/Api/EmailVerificationController.cs \
        ProjectCeres/ViewModels/Auth/EmailVerifyRequest.cs \
        ProjectCeres/ViewModels/Auth/EmailVerifyResendRequest.cs \
        ProjectCeres/Controllers/Api/AuthController.cs \
        ProjectCeres/Common/Authentication/FailedLoginReason.cs \
        ProjectCeres.Tests/Integration/EmailConfirmationTests.cs
git commit -m "feat(9.3): EmailVerificationController + Register/Login wiring + 14 integration tests"
```

---

## Task 7: Email resx entries

**Files:**
- Modify: `ProjectCeres/Common/Email/EmailTemplateKey.cs`
- Modify: `ProjectCeres/Resources/EmailsResource.en.resx`
- Modify: `ProjectCeres/Resources/EmailsResource.es.resx`

- [ ] **Step 1: Add `RegistrationConfirmation` to the enum**

```csharp
// ProjectCeres/Common/Email/EmailTemplateKey.cs
public enum EmailTemplateKey
{
    PasswordResetRequest,
    PasswordChanged,
    // ... existing values ...
    LockoutUnlock,
    RegistrationConfirmation,
}
```

- [ ] **Step 2: EN resx entries**

Append to `ProjectCeres/Resources/EmailsResource.en.resx`:

```xml
<data name="RegistrationConfirmation.Subject" xml:space="preserve">
  <value>Confirm your Ceres email address</value>
</data>
<data name="RegistrationConfirmation.BodyText" xml:space="preserve">
  <value>Welcome to Ceres. Click the link below within 30 minutes to confirm your email address and finish signing up:

{0}

If you didn't sign up, you can safely ignore this email.</value>
</data>
<data name="RegistrationConfirmation.BodyHtml" xml:space="preserve">
  <value>&lt;p&gt;Welcome to Ceres. &lt;a href="{0}"&gt;Confirm your email&lt;/a&gt; within 30 minutes to finish signing up.&lt;/p&gt;&lt;p&gt;If you didn't sign up, you can safely ignore this email.&lt;/p&gt;</value>
</data>
```

- [ ] **Step 3: ES resx entries**

Append to `ProjectCeres/Resources/EmailsResource.es.resx`:

```xml
<data name="RegistrationConfirmation.Subject" xml:space="preserve">
  <value>Confirma tu dirección de correo en Ceres</value>
</data>
<data name="RegistrationConfirmation.BodyText" xml:space="preserve">
  <value>Te damos la bienvenida a Ceres. Haz clic en el siguiente enlace dentro de los próximos 30 minutos para confirmar tu correo electrónico y terminar tu registro:

{0}

Si no creaste esta cuenta, puedes ignorar este mensaje.</value>
</data>
<data name="RegistrationConfirmation.BodyHtml" xml:space="preserve">
  <value>&lt;p&gt;Te damos la bienvenida a Ceres. &lt;a href="{0}"&gt;Confirma tu correo&lt;/a&gt; dentro de los próximos 30 minutos para terminar tu registro.&lt;/p&gt;&lt;p&gt;Si no creaste esta cuenta, puedes ignorar este mensaje.&lt;/p&gt;</value>
</data>
```

- [ ] **Step 4: Build to confirm resx parsing**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj
```

Expected: no resx parse errors.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Common/Email/EmailTemplateKey.cs \
        ProjectCeres/Resources/EmailsResource.en.resx \
        ProjectCeres/Resources/EmailsResource.es.resx
git commit -m "feat(9.3): RegistrationConfirmation email template (EN+ES)"
```

---

## Task 8: SPA register schema + i18n keys

**Files:**
- Create: `ProjectCeres.Client/src/app/auth/schemas/register.schema.ts`
- Modify: `ProjectCeres.Client/src/i18n/locales/en.json`
- Modify: `ProjectCeres.Client/src/i18n/locales/es.json`

- [ ] **Step 1: Schema**

```ts
// ProjectCeres.Client/src/app/auth/schemas/register.schema.ts
import { z } from 'zod';

export const registerSchema = z.object({
  email: z.string().email({ message: 'auth.register.errors.invalidEmail' }),
  password: z.string().min(8, { message: 'auth.register.errors.passwordTooShort' }),
});

export type RegisterFormValues = z.infer<typeof registerSchema>;
```

- [ ] **Step 2: i18n keys (EN)**

Under the existing `auth` block in `en.json`, add:

```json
"register": {
  "title": "Create your account",
  "description": "Sign up for Ceres. We'll send a verification email to confirm your address.",
  "emailLabel": "Email",
  "passwordLabel": "Password",
  "passwordHint": "At least 8 characters. Avoid passwords used in known data breaches.",
  "submit": "Create account",
  "submitting": "Creating account…",
  "successTitle": "Check your inbox",
  "successBody": "We've sent a verification link to {{email}}. Click the link to finish creating your account. The link expires in 30 minutes.",
  "backToSignIn": "Back to sign in",
  "errors": {
    "invalidEmail": "Enter a valid email address.",
    "passwordTooShort": "Password must be at least 8 characters.",
    "passwordBreached": "This password has appeared in a known data breach. Choose a different one.",
    "network": "Couldn't reach the server. Try again."
  }
},
"emailVerify": {
  "title": "Verify your email",
  "verifyingMessage": "Verifying your email address…",
  "successTitle": "Email verified",
  "successBody": "Your email is confirmed. You can now sign in.",
  "signInLink": "Go to sign in",
  "invalidTitle": "This verification link is invalid",
  "invalidBody": "The link may have expired or already been used. Request a new verification email below.",
  "resendButton": "Resend verification email",
  "resendPromptLabel": "Email",
  "resendSubmit": "Send link",
  "resendSubmitting": "Sending…",
  "resendSuccess": "If that email is registered, we've sent a new verification link.",
  "errors": {
    "network": "Couldn't reach the server. Try again.",
    "resendFailed": "Couldn't resend right now. Try again in a minute."
  }
}
```

Also extend `auth.login.errors.resendFailed` (already present) and add `auth.login.errors.resendTooMany` if not present.

- [ ] **Step 3: i18n keys (ES) — full Spanish translations**

Mirror the same structure under `es.json`'s `auth` block:

```json
"register": {
  "title": "Crea tu cuenta",
  "description": "Regístrate en Ceres. Te enviaremos un correo de verificación para confirmar tu dirección.",
  "emailLabel": "Correo electrónico",
  "passwordLabel": "Contraseña",
  "passwordHint": "Al menos 8 caracteres. Evita contraseñas que hayan aparecido en filtraciones conocidas.",
  "submit": "Crear cuenta",
  "submitting": "Creando cuenta…",
  "successTitle": "Revisa tu bandeja de entrada",
  "successBody": "Hemos enviado un enlace de verificación a {{email}}. Haz clic en el enlace para terminar de crear tu cuenta. El enlace caduca en 30 minutos.",
  "backToSignIn": "Volver a iniciar sesión",
  "errors": {
    "invalidEmail": "Introduce una dirección de correo válida.",
    "passwordTooShort": "La contraseña debe tener al menos 8 caracteres.",
    "passwordBreached": "Esta contraseña aparece en una filtración conocida. Elige otra.",
    "network": "No se pudo conectar con el servidor. Vuelve a intentarlo."
  }
},
"emailVerify": {
  "title": "Verifica tu correo",
  "verifyingMessage": "Verificando tu correo electrónico…",
  "successTitle": "Correo verificado",
  "successBody": "Tu correo electrónico está confirmado. Ya puedes iniciar sesión.",
  "signInLink": "Ir al inicio de sesión",
  "invalidTitle": "Este enlace de verificación no es válido",
  "invalidBody": "Es posible que el enlace haya caducado o que ya se haya usado. Solicita un nuevo correo de verificación más abajo.",
  "resendButton": "Reenviar correo de verificación",
  "resendPromptLabel": "Correo electrónico",
  "resendSubmit": "Enviar enlace",
  "resendSubmitting": "Enviando…",
  "resendSuccess": "Si ese correo está registrado, hemos enviado un nuevo enlace de verificación.",
  "errors": {
    "network": "No se pudo conectar con el servidor. Vuelve a intentarlo.",
    "resendFailed": "No se pudo reenviar ahora. Vuelve a intentarlo en un minuto."
  }
}
```

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/auth/schemas/register.schema.ts \
        ProjectCeres.Client/src/i18n/locales/en.json \
        ProjectCeres.Client/src/i18n/locales/es.json
git commit -m "feat(9.3): register + email-verify schema + i18n keys (EN+ES)"
```

---

## Task 9: `Register.tsx` (TDD)

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/auth/Register.test.tsx`
- Create: `ProjectCeres.Client/src/app/pages/auth/Register.tsx`
- Modify: `ProjectCeres.Client/src/app/App.tsx`

- [ ] **Step 1: Write failing tests (RED)**

Use the same MSW pattern as `PasswordReset.test.tsx`. Five tests:

```tsx
// Register.test.tsx (abridged)
import { describe, test, expect, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Register } from './Register';
import { renderWithProviders } from '../../testing/providers';
import { server } from '../../testing/msw-server';
import { http, HttpResponse } from 'msw';

describe('Register', () => {
  test('renders email + password + submit + back-to-sign-in', () => {
    renderWithProviders(<Register />);
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /create account/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to sign in/i })).toBeInTheDocument();
  });

  test('204 replaces form with success block referencing the typed email', async () => {
    server.use(http.post('/api/auth/register', () => new HttpResponse(null, { status: 204 })));
    renderWithProviders(<Register />);
    await userEvent.type(screen.getByLabelText(/email/i), 'foo@example.com');
    await userEvent.type(screen.getByLabelText(/password/i), 'ValidPass!2026');
    await userEvent.click(screen.getByRole('button', { name: /create account/i }));
    expect(await screen.findByText(/check your inbox/i)).toBeInTheDocument();
    expect(screen.getByText(/foo@example.com/)).toBeInTheDocument();
  });

  test('422 password policy violation → field error', async () => {
    server.use(http.post('/api/auth/register', () => HttpResponse.json({
      error: { code: 'VALIDATION_ERROR', details: [{ field: 'Password', message: 'auth.register.errors.passwordBreached' }] }
    }, { status: 422 })));
    renderWithProviders(<Register />);
    await userEvent.type(screen.getByLabelText(/email/i), 'foo@example.com');
    await userEvent.type(screen.getByLabelText(/password/i), 'ValidPass!2026');
    await userEvent.click(screen.getByRole('button', { name: /create account/i }));
    expect(await screen.findByText(/breach/i)).toBeInTheDocument();
  });

  test('network error → inline error', async () => {
    server.use(http.post('/api/auth/register', () => HttpResponse.error()));
    renderWithProviders(<Register />);
    await userEvent.type(screen.getByLabelText(/email/i), 'foo@example.com');
    await userEvent.type(screen.getByLabelText(/password/i), 'ValidPass!2026');
    await userEvent.click(screen.getByRole('button', { name: /create account/i }));
    expect(await screen.findByText(/couldn't reach/i)).toBeInTheDocument();
  });

  test('zod: password < 8 chars → field error, no fetch', async () => {
    let calls = 0;
    server.use(http.post('/api/auth/register', () => { calls++; return new HttpResponse(null, { status: 204 }); }));
    renderWithProviders(<Register />);
    await userEvent.type(screen.getByLabelText(/email/i), 'foo@example.com');
    await userEvent.type(screen.getByLabelText(/password/i), 'short');
    await userEvent.click(screen.getByRole('button', { name: /create account/i }));
    expect(await screen.findByText(/at least 8 characters/i)).toBeInTheDocument();
    expect(calls).toBe(0);
  });
});
```

- [ ] **Step 2: Run tests — expect RED**

```bash
pnpm --dir ProjectCeres.Client test Register.test
```

Expected: 5 failures (`Register` not exported).

- [ ] **Step 3: Implement `Register.tsx`**

Mirror `PasswordReset.tsx`'s `RequestForm` structure. The implementation should:
- Use `react-hook-form` + `zodResolver(registerSchema)`.
- POST to `/api/auth/register` via `apiFetch`.
- On 204: set `submitted = true`, render the success block with the typed email.
- On 422: read `error.details[]`, map each field to a server error via `form.setError`.
- On network error: render an inline error block.
- Use `<Field>` recipe for both inputs.
- "Back to sign in" link to `/login`.

```tsx
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Field } from '../../components/Field';
import { registerSchema, type RegisterFormValues } from '../../auth/schemas/register.schema';
import { apiFetch } from '../../lib/api-client';

export function Register() {
  const { t } = useTranslation();
  const [submitted, setSubmitted] = useState<{ email: string } | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const form = useForm<RegisterFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { email: '', password: '' },
  });

  const onSubmit = async (values: RegisterFormValues) => {
    setErrorMessage(null);
    const result = await apiFetch('/api/auth/register', {
      method: 'POST',
      body: { email: values.email, password: values.password },
    });
    if (result.ok) {
      setSubmitted({ email: values.email });
      return;
    }
    if (result.status === 422) {
      const details = (result.body as { error?: { details?: Array<{ field: string; message: string }> } })
        ?.error?.details ?? [];
      for (const d of details) {
        // Map server-side field name "Password" → form field "password"
        const formField = d.field.toLowerCase() as keyof RegisterFormValues;
        form.setError(formField, { type: 'server', message: t(d.message) });
      }
      return;
    }
    setErrorMessage(t('auth.register.errors.network'));
  };

  if (submitted) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold tracking-tight">{t('auth.register.successTitle')}</h1>
        <p className="text-sm text-muted-foreground">
          {t('auth.register.successBody', { email: submitted.email })}
        </p>
        <div className="text-sm">
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t('auth.register.backToSignIn')}
          </Link>
        </div>
      </div>
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.register.title')}</h1>
      <p className="text-sm text-muted-foreground">{t('auth.register.description')}</p>

      <Field
        label={t('auth.register.emailLabel')}
        htmlFor="email"
        error={form.formState.errors.email?.message ? t(form.formState.errors.email.message) : undefined}
      >
        <Input id="email" type="email" autoComplete="email" autoFocus {...form.register('email')} />
      </Field>

      <Field
        label={t('auth.register.passwordLabel')}
        htmlFor="password"
        hint={t('auth.register.passwordHint')}
        error={form.formState.errors.password?.message ? t(form.formState.errors.password.message) : undefined}
      >
        <Input id="password" type="password" autoComplete="new-password" {...form.register('password')} />
      </Field>

      {errorMessage && (
        <div role="alert" aria-live="polite" className="text-sm text-destructive">{errorMessage}</div>
      )}

      <Button type="submit" className="w-full" disabled={form.formState.isSubmitting}>
        {form.formState.isSubmitting ? t('auth.register.submitting') : t('auth.register.submit')}
      </Button>

      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.register.backToSignIn')}
        </Link>
      </div>
    </form>
  );
}
```

- [ ] **Step 4: Route in App.tsx**

```tsx
// Replace: <Route path="register" element={<RegisterPlaceholder />} />
// With:    <Route path="register" element={<Register />} />
```

Add the import at the top.

- [ ] **Step 5: Run tests — expect GREEN**

```bash
pnpm --dir ProjectCeres.Client test Register.test
```

Expected: 5/5 passing.

- [ ] **Step 6: Delete `RegisterPlaceholder.tsx` + remove its i18n keys**

Remove the dead `auth.placeholders.register.*` block from `en.json` + `es.json`.

```bash
rm ProjectCeres.Client/src/app/pages/auth/RegisterPlaceholder.tsx
```

Remove the `RegisterPlaceholder` import from `App.tsx`.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/auth/Register.tsx \
        ProjectCeres.Client/src/app/pages/auth/Register.test.tsx \
        ProjectCeres.Client/src/app/App.tsx \
        ProjectCeres.Client/src/i18n/
git rm ProjectCeres.Client/src/app/pages/auth/RegisterPlaceholder.tsx
git commit -m "feat(9.3): Register page (TDD, 5 tests), delete RegisterPlaceholder"
```

---

## Task 10: `EmailVerify.tsx` (TDD)

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/auth/EmailVerify.test.tsx`
- Create: `ProjectCeres.Client/src/app/pages/auth/EmailVerify.tsx`
- Modify: `ProjectCeres.Client/src/app/App.tsx`
- Modify: `ProjectCeres.Client/src/app/pages/auth/PasswordReset.tsx` (extract `readTokenFromHash`)

- [ ] **Step 1: Extract `readTokenFromHash` to a shared module**

Create `ProjectCeres.Client/src/app/lib/url-hash-token.ts`:

```ts
export function readTokenFromHash(hash: string): string | null {
  const stripped = hash.startsWith('#') ? hash.slice(1) : hash;
  if (!stripped) return null;
  const params = new URLSearchParams(stripped);
  const token = params.get('token');
  return token && token.length > 0 ? token : null;
}
```

Update `PasswordReset.tsx` to import from this module instead of defining locally. Run the existing PasswordReset tests to confirm no regression:

```bash
pnpm --dir ProjectCeres.Client test PasswordReset.test
```

Expected: existing PasswordReset tests still pass.

- [ ] **Step 2: Write failing EmailVerify tests (RED)**

Five tests per spec § SPA tests:

```tsx
// EmailVerify.test.tsx (abridged)
describe('EmailVerify', () => {
  test('no hash → invalid-link error block + resend button', () => {
    renderWithProviders(<EmailVerify />, { initialEntries: ['/email-verify'] });
    expect(screen.getByText(/this verification link is invalid/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /resend/i })).toBeInTheDocument();
  });

  test('token in hash → fires POST /api/auth/email/verify with that token', async () => {
    let captured: string | undefined;
    server.use(http.post('/api/auth/email/verify', async ({ request }) => {
      const body = (await request.json()) as { token: string };
      captured = body.token;
      return new HttpResponse(null, { status: 204 });
    }));
    renderWithProviders(<EmailVerify />, { initialEntries: ['/email-verify#token=abc123'] });
    await screen.findByText(/email verified/i);
    expect(captured).toBe('abc123');
  });

  test('204 → success block + sign-in link', async () => {
    server.use(http.post('/api/auth/email/verify', () => new HttpResponse(null, { status: 204 })));
    renderWithProviders(<EmailVerify />, { initialEntries: ['/email-verify#token=abc'] });
    expect(await screen.findByText(/email verified/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /go to sign in/i })).toBeInTheDocument();
  });

  test('401 INVALID_VERIFICATION_TOKEN → invalid-link error block + resend', async () => {
    server.use(http.post('/api/auth/email/verify', () => HttpResponse.json({
      error: { code: 'INVALID_VERIFICATION_TOKEN', message: '' },
    }, { status: 401 })));
    renderWithProviders(<EmailVerify />, { initialEntries: ['/email-verify#token=bad'] });
    expect(await screen.findByText(/this verification link is invalid/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /resend/i })).toBeInTheDocument();
  });

  test('resend → POST /api/auth/email/verify/resend → success acknowledgement', async () => {
    server.use(
      http.post('/api/auth/email/verify', () => HttpResponse.json({ error: { code: 'INVALID_VERIFICATION_TOKEN' } }, { status: 401 })),
      http.post('/api/auth/email/verify/resend', () => new HttpResponse(null, { status: 204 })),
    );
    renderWithProviders(<EmailVerify />, { initialEntries: ['/email-verify#token=bad'] });
    await screen.findByRole('button', { name: /resend/i });
    await userEvent.click(screen.getByRole('button', { name: /resend/i }));
    await userEvent.type(screen.getByLabelText(/email/i), 'foo@example.com');
    await userEvent.click(screen.getByRole('button', { name: /send link/i }));
    expect(await screen.findByText(/we've sent a new verification link/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Run tests — expect RED**

```bash
pnpm --dir ProjectCeres.Client test EmailVerify.test
```

Expected: 5 failures (`EmailVerify` not exported).

- [ ] **Step 4: Implement `EmailVerify.tsx`**

Three states: verifying / success / invalid. On invalid, show resend form. Sketch:

```tsx
import { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Field } from '../../components/Field';
import { readTokenFromHash } from '../../lib/url-hash-token';
import { apiFetch } from '../../lib/api-client';

const resendSchema = z.object({
  email: z.string().email({ message: 'auth.emailVerify.errors.network' }),
});

export function EmailVerify() {
  const { t } = useTranslation();
  const location = useLocation();
  const token = useMemo(() => readTokenFromHash(location.hash), [location.hash]);
  const [state, setState] = useState<'verifying' | 'success' | 'invalid'>(
    token ? 'verifying' : 'invalid'
  );

  useEffect(() => {
    if (state !== 'verifying' || !token) return;
    let cancelled = false;
    (async () => {
      const result = await apiFetch('/api/auth/email/verify', {
        method: 'POST',
        body: { token },
      });
      if (cancelled) return;
      setState(result.ok ? 'success' : 'invalid');
    })();
    return () => { cancelled = true; };
  }, [state, token]);

  if (state === 'verifying') {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold tracking-tight">{t('auth.emailVerify.title')}</h1>
        <p className="text-sm text-muted-foreground" role="status" aria-live="polite">
          {t('auth.emailVerify.verifyingMessage')}
        </p>
      </div>
    );
  }
  if (state === 'success') {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold tracking-tight">{t('auth.emailVerify.successTitle')}</h1>
        <p className="text-sm text-muted-foreground">{t('auth.emailVerify.successBody')}</p>
        <div className="text-sm">
          <Link to="/login" className="underline-offset-4 hover:underline">
            {t('auth.emailVerify.signInLink')}
          </Link>
        </div>
      </div>
    );
  }
  return <InvalidBlock />;
}

function InvalidBlock() {
  const { t } = useTranslation();
  const [resendMode, setResendMode] = useState<'idle' | 'form' | 'sent'>('idle');

  const form = useForm({ resolver: zodResolver(resendSchema), defaultValues: { email: '' } });

  const onResend = async (values: { email: string }) => {
    const result = await apiFetch('/api/auth/email/verify/resend', {
      method: 'POST',
      body: { email: values.email },
    });
    // Anti-enum: server always returns 204; UX is the same regardless.
    if (result.ok || result.status === 204) setResendMode('sent');
  };

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold tracking-tight">{t('auth.emailVerify.invalidTitle')}</h1>
      <p className="text-sm text-muted-foreground" role="alert">{t('auth.emailVerify.invalidBody')}</p>
      {resendMode === 'idle' && (
        <Button onClick={() => setResendMode('form')} className="w-full">
          {t('auth.emailVerify.resendButton')}
        </Button>
      )}
      {resendMode === 'form' && (
        <form onSubmit={form.handleSubmit(onResend)} className="space-y-3" noValidate>
          <Field
            label={t('auth.emailVerify.resendPromptLabel')}
            htmlFor="resend-email"
            error={form.formState.errors.email?.message ? t(form.formState.errors.email.message) : undefined}
          >
            <Input id="resend-email" type="email" autoComplete="email" autoFocus {...form.register('email')} />
          </Field>
          <Button type="submit" className="w-full" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting
              ? t('auth.emailVerify.resendSubmitting')
              : t('auth.emailVerify.resendSubmit')}
          </Button>
        </form>
      )}
      {resendMode === 'sent' && (
        <p className="text-sm text-muted-foreground" role="status" aria-live="polite">
          {t('auth.emailVerify.resendSuccess')}
        </p>
      )}
      <div className="text-sm">
        <Link to="/login" className="underline-offset-4 hover:underline">
          {t('auth.emailVerify.signInLink')}
        </Link>
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Add route to App.tsx**

```tsx
<Route path="email-verify" element={<EmailVerify />} />
```

- [ ] **Step 6: Run tests — expect GREEN**

```bash
pnpm --dir ProjectCeres.Client test EmailVerify.test
```

Expected: 5/5 passing.

- [ ] **Step 7: Wire `onResendVerification` in `Login.tsx`**

Replace the placeholder body with the D-resend logic from the spec (read email from `form.getValues('email')`, POST `{email}`, surface `resendSucceeded` state for the success message).

- [ ] **Step 8: Run full SPA suite**

```bash
pnpm --dir ProjectCeres.Client test
pnpm --dir ProjectCeres.Client build
```

Expected: green.

- [ ] **Step 9: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/auth/EmailVerify.tsx \
        ProjectCeres.Client/src/app/pages/auth/EmailVerify.test.tsx \
        ProjectCeres.Client/src/app/pages/auth/PasswordReset.tsx \
        ProjectCeres.Client/src/app/pages/auth/Login.tsx \
        ProjectCeres.Client/src/app/lib/url-hash-token.ts \
        ProjectCeres.Client/src/app/App.tsx
git commit -m "feat(9.3): EmailVerify page + readTokenFromHash extraction + Login resend wiring"
```

---

## Task 11: Full-suite regression + manual verification handoff

- [ ] **Step 1: Run full backend suite**

```bash
dotnet test
```

Expected: 1098 baseline + 14 new EmailConfirmation tests = 1112 passing. Investigate any regression.

- [ ] **Step 2: Run full SPA suite**

```bash
pnpm --dir ProjectCeres.Client test
pnpm --dir ProjectCeres.Client build
```

Expected: green.

- [ ] **Step 3: Manual browser handoff**

Hand the user this checklist (after auditing that every entry point exists in committed code — Phase H gate):

**Prerequisites audit:**
- `/register` route mounted ✓ (App.tsx)
- `/email-verify` route mounted ✓ (App.tsx)
- `/login` displays "Create account" link ✓ (already present pre-9.3)
- `EMAIL_NOT_CONFIRMED` server branch active ✓ (Task 6 step 4)
- MailDrop folder receives emails ✓ (existing IEmailService config)

**Steps:**
1. Open `/register` → form renders with email + password + Create account button.
2. Submit a fresh email + valid password → form replaced by "Check your inbox" block.
3. Open MailDrop (`./mail-out/`) → email present with link `${origin}/email-verify#token=...`.
4. Click the link → page renders "Verifying your email" briefly, then "Email verified" + sign-in link.
5. Navigate to `/login` → log in with the just-verified credentials → reaches dashboard.
6. Open `/register` again with the SAME email → 204, success-block UX (no leak that email exists).
7. Open `/login` with a fresh-registered email **before** verifying → "Verify your email before signing in" + Resend link.
8. Click Resend → "If that email is registered, we've sent a new link" success block.
9. At 375px mobile: each page fits without horizontal overflow; touch targets ≥ 44×44px.
10. Toggle language to ES via the globe → entire flow renders in Spanish (no English leaks).
11. Open `/email-verify` with NO hash → invalid-link block + Resend button.
12. Open `/email-verify#token=garbage` → 401 → invalid-link block + Resend.

---

## Self-review

**Spec coverage:**
- ✅ Entity + migration → Tasks 1, 2
- ✅ Generator + Service → Tasks 3, 4, 5
- ✅ Controller + DTOs → Task 6
- ✅ Register handler three-branch shape → Task 6 step 3
- ✅ Login IsNotAllowed branch → Task 6 step 4
- ✅ Resx EN+ES → Task 7
- ✅ Register SPA + tests → Task 9
- ✅ EmailVerify SPA + tests → Task 10
- ✅ Login resend wiring → Task 10 step 7
- ✅ readTokenFromHash extraction → Task 10 step 1
- ✅ All 14 backend tests → Task 5 step 1
- ✅ All 10 SPA tests (5 Register + 5 EmailVerify) → Tasks 9, 10
- ✅ Manual verification checklist → Task 11

**Placeholder scan:** none.

**Type consistency:** `EmailConfirmationService.IssueAsync(Guid, string, string, CancellationToken)` used consistently across Task 5, Task 6 step 3. `EmailConfirmationConfirmOutcome.Success(Guid UserId)` matches the controller's switch.

Plan saved to `docs/superpowers/plans/2026-05-22-stage-9-3-register-and-email-verify-impl.md`. Plan for 9.5 follows separately.
