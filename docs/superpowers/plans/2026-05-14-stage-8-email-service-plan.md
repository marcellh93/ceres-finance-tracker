# Stage 8 — Email Service + Email Security Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the dev-only `LogOnlyEmailService` with a production Resend integration, move the nine hard-coded email bodies in `PasswordResetService` / `EmailChangeService` / `LockoutUnlockService` into EN+ES `.resx` files behind an `IEmailComposer`, enforce the security-model § Layer 2 recipient lock at compile time via an `EmailRecipient` value type, add per-user + per-IP rate limiting on every email-triggering endpoint, ingest Resend delivery webhooks into a new `EmailDeliveryEvent` table, and ship the SPF/DKIM/DMARC publication runbook (DNS records themselves deferred to Stage 16 — no domain registered yet).

**Architecture:** Four collaborators in `ProjectCeres.Common.Email`: `IEmailService` (existing entry point; new `ResendEmailService` impl), `IEmailComposer` (resx-backed template renderer), `IEmailRecipientResolver` (single source of truth for `To:`), `ILanguageResolver` (reads `Settings.Language` for the recipient user). `Resources/EmailsResource.cs` marker type + `Emails.en.resx` / `Emails.es.resx` hold all 27 keys (9 templates × Subject/BodyText/BodyHtml). New `ResendWebhookController` accepts Svix-signed delivery events. Six sub-stages (8a–8f) commit independently; the suite stays green between them.

**Tech Stack:** .NET 10 / ASP.NET Core MVC, EF Core + PostgreSQL, `Resend` NuGet SDK, `System.Threading.RateLimiting` (existing), `Microsoft.Extensions.Localization` (`IStringLocalizer`), xUnit + Moq + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-05-14-stage-8-email-service-design.md`.

---

## File Structure

### New files

```
ProjectCeres/
  Common/Email/
    EmailRecipient.cs                 ← value type w/ FromVerifiedUser + OverrideForEmailChange factories
    EmailRecipientResolver.cs         ← IEmailRecipientResolver + impl (reads ApplicationUser.Email)
    EmailRecipientNotResolvableException.cs
    EmailTemplateKey.cs               ← enum (9 values)
    EmailComposer.cs                  ← IEmailComposer + impl (resx → EmailMessage)
    EmailOptions.cs                   ← config record (Email:Resend:*)
    LanguageResolver.cs               ← ILanguageResolver + impl
    ResendEmailService.cs             ← IEmailService impl wrapping IResend
    ResendSignatureVerifier.cs        ← Svix HMAC verification
    EmailDeliveryEvent.cs             ← entity (IUserOwned, nullable UserId)
  Resources/
    EmailsResource.cs                 ← marker type (one-line class)
    Emails.en.resx                    ← 27 keys
    Emails.es.resx                    ← 27 keys
  Controllers/Api/
    ResendWebhookController.cs        ← POST /api/internal/email-webhook/resend

ProjectCeres/Migrations/
  YYYYMMDDHHMMSS_AddSettingsLanguageColumn.cs        ← Task 2
  YYYYMMDDHHMMSS_AddEmailDeliveryEvent.cs            ← Task 5

ProjectCeres.Tests/Integration/Email/                ← new folder for email-domain tests
  EmailRecipientTests.cs
  EmailComposerTests.cs
  LanguageResolverTests.cs
  ResendEmailServiceTests.cs
  EmailRateLimitTests.cs
  ResendWebhookTests.cs

docs/runbooks/
  email-dns-setup.md                  ← Task 6
```

### Modified files

```
ProjectCeres/
  ProjectCeres.csproj                                       ← add Resend NuGet
  Program.cs                                                ← bind EmailOptions, conditional IEmailService registration, add EmailByUser + EmailByIp policies, register IEmailComposer / IEmailRecipientResolver / ILanguageResolver
  Common/Email/EmailMessage.cs                              ← To: string → EmailRecipient
  Common/Authentication/PasswordResetService.cs             ← delete 3 Build*Email helpers, call IEmailComposer
  Common/Authentication/EmailChangeService.cs               ← delete 5 Build*Email helpers, call IEmailComposer
  Common/Authentication/LockoutUnlockService.cs             ← delete BuildLockoutEmail, call IEmailComposer
  Common/Authentication/AuthRateLimitPolicies.cs            ← add EmailByUser, EmailByIp constants
  Models/Settings.cs                                        ← add Language column
docs/
  planning-phase3.md                                        ← mark localization email plumbing ✅; Batch 3d row ✅
  roadmap-phase-three.md                                    ← Stage 8 checklist items resolved/deferred; Stage 16 gets new DNS publication line
  security-model.md                                         ← cross-link runbook from § Email Security Rules
  models.md                                                 ← add EmailDeliveryEvent
```

---

## Task 1 — Sub-stage 8a: `EmailRecipient` value type and resolver

**Goal:** Move the `To:` address off of caller-supplied strings into a constructor-locked value type read from `ApplicationUser.Email`. Existing 9 `Build*Email` helpers keep their inline copy; they now construct `EmailMessage` with an `EmailRecipient` instead of a `string`. Suite stays green throughout.

**Files:**
- Create: `ProjectCeres/Common/Email/EmailRecipient.cs`
- Create: `ProjectCeres/Common/Email/EmailRecipientResolver.cs`
- Create: `ProjectCeres/Common/Email/EmailRecipientNotResolvableException.cs`
- Modify: `ProjectCeres/Common/Email/EmailMessage.cs`
- Modify: `ProjectCeres/Common/Email/LogOnlyEmailService.cs` (To prints as `message.To.Address`)
- Modify: `ProjectCeres/Common/Authentication/PasswordResetService.cs` (3 Build*Email call sites)
- Modify: `ProjectCeres/Common/Authentication/EmailChangeService.cs` (5 Build*Email call sites)
- Modify: `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` (1 Build*Email call site)
- Modify: `ProjectCeres/Program.cs` (register `IEmailRecipientResolver`)
- Test: `ProjectCeres.Tests/Integration/Email/EmailRecipientTests.cs`

- [ ] **Step 1: Write the failing reflection-based recipient-lock tests**

Create `ProjectCeres.Tests/Integration/Email/EmailRecipientTests.cs`:

```csharp
using System.Reflection;
using FluentAssertions;
using ProjectCeres.Common.Email;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

public sealed class EmailRecipientTests
{
    [Fact]
    public void EmailMessage_has_no_public_string_To_constructor()
    {
        // Pins the compile-time recipient lock from spec § Architecture / EmailRecipient.
        // A regression here would mean a service could re-introduce caller-supplied
        // To: addresses, breaking the security-model § Layer 2 recipient-lock rule.
        var publicCtors = typeof(EmailMessage).GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        foreach (var ctor in publicCtors)
        {
            var firstParam = ctor.GetParameters().FirstOrDefault();
            firstParam.Should().NotBeNull();
            firstParam!.ParameterType.Should().Be(typeof(EmailRecipient),
                "the first constructor parameter of EmailMessage must be EmailRecipient, " +
                "not string — otherwise services can bypass the recipient lock.");
        }
    }

    [Fact]
    public void EmailRecipient_OverrideForEmailChange_is_called_only_by_EmailChangeService()
    {
        // Pins the spec § Architecture audit story: only EmailChangeService is allowed
        // to call OverrideForEmailChange. A new caller is either the email-change flow
        // (in which case rename or extend the factory + update this test) or a security
        // regression that must be reverted.
        var asm = typeof(EmailRecipient).Assembly;
        var override_ = typeof(EmailRecipient).GetMethod(
            "OverrideForEmailChange",
            BindingFlags.Static | BindingFlags.NonPublic);
        override_.Should().NotBeNull();

        // We grep the on-disk source rather than reflecting over IL: the IL doesn't carry
        // a stable indication of which type contains a call to an internal static method
        // without a full MethodBody walk, and this test is meant to be cheap.
        var projectRoot = AppContext.BaseDirectory;
        // Walk up until we find ProjectCeres/Common/Authentication/EmailChangeService.cs
        var dir = new DirectoryInfo(projectRoot);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ProjectCeres", "Common", "Authentication")))
            dir = dir.Parent;
        dir.Should().NotBeNull("test must be able to locate the ProjectCeres source tree");

        var authDir = Path.Combine(dir!.FullName, "ProjectCeres", "Common", "Authentication");
        var matches = Directory.EnumerateFiles(authDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => File.ReadAllText(f).Contains("OverrideForEmailChange", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        matches.Should().BeEquivalentTo(new[] { "EmailChangeService.cs" });
    }
}
```

- [ ] **Step 2: Run the failing tests**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~EmailRecipientTests" --logger "console;verbosity=minimal"`
Expected: build fails — `EmailRecipient` type does not exist yet.

- [ ] **Step 3: Create `EmailRecipientNotResolvableException`**

Create `ProjectCeres/Common/Email/EmailRecipientNotResolvableException.cs`:

```csharp
namespace ProjectCeres.Common.Email;

public sealed class EmailRecipientNotResolvableException : Exception
{
    public Guid UserId { get; }

    public EmailRecipientNotResolvableException(Guid userId, string reason)
        : base($"Could not resolve email recipient for user {userId}: {reason}")
    {
        UserId = userId;
    }
}
```

- [ ] **Step 4: Create `EmailRecipient`**

Create `ProjectCeres/Common/Email/EmailRecipient.cs`:

```csharp
namespace ProjectCeres.Common.Email;

/// <summary>
/// Compile-time enforcement of the security-model § Email Security Rules § Layer 2
/// recipient-lock rule: every outgoing email's `To:` address is constructed via this
/// type, never from a raw string. The only legitimate factories are
/// <see cref="FromVerifiedUser"/> (server-resolved from <c>ApplicationUser.Email</c>)
/// and <see cref="OverrideForEmailChange"/> (server-resolved from
/// <c>EmailChangeToken</c> — only the email-change flow uses this).
/// </summary>
public sealed class EmailRecipient
{
    public string Address { get; }

    private EmailRecipient(string address) => Address = address;

    /// <summary>Placeholder used by IEmailComposer before the resolver populates the To slot.</summary>
    internal static readonly EmailRecipient None = new("");

    internal static EmailRecipient FromVerifiedUser(string email) => new(email);

    /// <summary>
    /// Email-change flow only. The user's current <c>ApplicationUser.Email</c> is the new
    /// address; notifications to the OLD address are sent via this factory. The old
    /// address is read server-side from <c>EmailChangeToken</c>, never from a request
    /// payload. EmailRecipientTests pins that only EmailChangeService calls this method.
    /// </summary>
    internal static EmailRecipient OverrideForEmailChange(string oldEmail) => new(oldEmail);

    public override string ToString() => Address;
}
```

- [ ] **Step 5: Change `EmailMessage.To` from `string` to `EmailRecipient`**

Modify `ProjectCeres/Common/Email/EmailMessage.cs`:

```csharp
namespace ProjectCeres.Common.Email;

public sealed record EmailMessage(EmailRecipient To, string Subject, string BodyHtml, string BodyText);
```

- [ ] **Step 6: Update `LogOnlyEmailService` to print `message.To.Address`**

Modify `ProjectCeres/Common/Email/LogOnlyEmailService.cs` line 19:

```csharp
_logger.LogInformation(
    "[email/dev] To={To} Subject={Subject} BodyText={BodyText}",
    message.To.Address, message.Subject, message.BodyText);
```

- [ ] **Step 7: Create `IEmailRecipientResolver` + impl**

Create `ProjectCeres/Common/Email/EmailRecipientResolver.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;

namespace ProjectCeres.Common.Email;

public interface IEmailRecipientResolver
{
    Task<EmailRecipient> ResolveAsync(Guid userId, CancellationToken ct);
}

public sealed class EmailRecipientResolver : IEmailRecipientResolver
{
    private readonly ApplicationDbContext _db;

    public EmailRecipientResolver(ApplicationDbContext db) => _db = db;

    public async Task<EmailRecipient> ResolveAsync(Guid userId, CancellationToken ct)
    {
        // Cross-tenant by design: this is called from pre-auth call sites (e.g. password
        // reset triggered before the user is fully signed in) AND from authenticated
        // contexts. Reading ApplicationUser.Email is safe because the userId always came
        // from server-side context (session, token row, audit context) — never from a
        // request payload. Stage 10 architecture test allow-lists this file.
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.EmailConfirmed })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            throw new EmailRecipientNotResolvableException(userId, "user not found");
        if (string.IsNullOrWhiteSpace(user.Email))
            throw new EmailRecipientNotResolvableException(userId, "user has no email");

        return EmailRecipient.FromVerifiedUser(user.Email!);
    }
}
```

- [ ] **Step 8: Register the resolver in `Program.cs`**

Modify `ProjectCeres/Program.cs` — add near the existing `IUserJobRunner` registration around line 63:

```csharp
builder.Services.AddScoped<IEmailRecipientResolver, EmailRecipientResolver>();
```

Required `using` at the top of the file: `using ProjectCeres.Common.Email;` (add if not already present).

- [ ] **Step 9: Migrate all `Build*Email` call sites to use `EmailRecipient` in their `EmailMessage` constructor calls**

For every line of the form `return new EmailMessage(<string>, subject, bodyHtml, bodyText);` in the three services, wrap the string with the correct factory:

In `PasswordResetService.cs`:
- Line 211 (`BuildRequestEmail`): `return new EmailMessage(EmailRecipient.FromVerifiedUser(to), subject, bodyHtml, bodyText);`
- Line 423 (`BuildEmailChangeCancelledByPasswordResetEmail`): same `FromVerifiedUser` wrap.
- Line ~441 (`BuildChangedEmail`): same `FromVerifiedUser` wrap.

In `EmailChangeService.cs`:
- Line 474 (`BuildVerifyNewEmail`, sent to NEW address — note: the new address is not yet in `user.Email`, so this is still a server-derived string and must use `OverrideForEmailChange` to bypass the resolver since the email isn't a registered user yet):
  `return new EmailMessage(EmailRecipient.OverrideForEmailChange(newEmail), subject, bodyHtml, bodyText);`
- Line 497 (`BuildRevokeOldEmail`, sent to OLD address): `OverrideForEmailChange(oldEmail)`.
- Line 515 (`BuildChangeConfirmedEmail`, sent to new which is now `user.Email`): `OverrideForEmailChange(newEmail)` — the parameter is the post-change email; in Task 2 the call site will use the resolver instead.
- Line 535 (`BuildChangeConfirmedToOldEmail`): `OverrideForEmailChange(oldEmail)`.
- Line 553 (`BuildRevokeNotificationToOldEmail`): `OverrideForEmailChange(oldEmail)`.

In `LockoutUnlockService.cs`:
- Line 201 (`BuildLockoutEmail`): `FromVerifiedUser(to)`.

(`FromVerifiedUser` and `OverrideForEmailChange` are `internal` — they're visible from within the `ProjectCeres` assembly.)

- [ ] **Step 10: Build**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: succeeds. If a `new EmailMessage(<string>, ...)` is missed, it fails with `cannot convert from 'string' to 'ProjectCeres.Common.Email.EmailRecipient'` — fix the missing call site and rebuild.

- [ ] **Step 11: Run the new reflection tests + full Authentication suite**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~Authentication|FullyQualifiedName~EmailRecipientTests" --logger "console;verbosity=minimal"`
Expected: all tests pass, including the two new EmailRecipient tests. Failing tests indicate a missed Build*Email call site or a regression in pre-existing email assertions (most existing tests assert against `message.To` as a string — those assertions need to compare against `message.To.Address` now).

If any pre-existing test fails with "string vs EmailRecipient" comparison: update the test to compare `.To.Address` to the expected string. This is a mechanical fix, not a behavior change.

- [ ] **Step 12: Commit**

```bash
git -C <repo> add -A
git -C <repo> commit -m "feat(stage-8a): EmailRecipient value type — compile-time recipient lock

Adds ProjectCeres.Common.Email.EmailRecipient with two internal factories
(FromVerifiedUser, OverrideForEmailChange). EmailMessage.To changes from
string to EmailRecipient — every outgoing email's To: address is now
constructed via a server-resolved factory, never a raw caller-supplied
string. Enforces the security-model § Email Security Rules § Layer 2
recipient-lock at compile time.

Adds IEmailRecipientResolver + EmailRecipientResolver (reads
ApplicationUser.Email by user id, throws if user has no email).

Tests:
- EmailMessage_has_no_public_string_To_constructor (reflection)
- EmailRecipient_OverrideForEmailChange_is_called_only_by_EmailChangeService"
```

---

## Task 2 — Sub-stage 8b: Resx templates, EmailComposer, LanguageResolver, Settings.Language column

**Goal:** Move all 9 hard-coded email subject/body strings from `PasswordResetService` / `EmailChangeService` / `LockoutUnlockService` into EN+ES `.resx` files. Composer reads `Settings.Language` for the recipient user, falls back to `en`. The 9 `Build*Email` helpers all delete.

**Files:**
- Create: `ProjectCeres/Resources/EmailsResource.cs`
- Create: `ProjectCeres/Resources/Emails.en.resx`
- Create: `ProjectCeres/Resources/Emails.es.resx`
- Create: `ProjectCeres/Common/Email/EmailTemplateKey.cs`
- Create: `ProjectCeres/Common/Email/EmailComposer.cs`
- Create: `ProjectCeres/Common/Email/LanguageResolver.cs`
- Create: `ProjectCeres/Migrations/YYYYMMDDHHMMSS_AddSettingsLanguageColumn.cs` (generated)
- Modify: `ProjectCeres/Models/Settings.cs` (add `Language` column)
- Modify: `ProjectCeres/Program.cs` (register localization + composer + language resolver)
- Modify: `ProjectCeres/Common/Authentication/PasswordResetService.cs` (delete 3 Build helpers, call composer)
- Modify: `ProjectCeres/Common/Authentication/EmailChangeService.cs` (delete 5 Build helpers, call composer)
- Modify: `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` (delete BuildLockoutEmail, call composer)
- Test: `ProjectCeres.Tests/Integration/Email/EmailComposerTests.cs`
- Test: `ProjectCeres.Tests/Integration/Email/LanguageResolverTests.cs`

- [ ] **Step 1: Write the failing EmailComposer test suite**

Create `ProjectCeres.Tests/Integration/Email/EmailComposerTests.cs`:

```csharp
using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Email;
using ProjectCeres.Tests.Integration;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

[Collection(nameof(IntegrationTestCollection))]
public sealed class EmailComposerTests
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailComposerTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData(EmailTemplateKey.PasswordResetRequest, "en")]
    [InlineData(EmailTemplateKey.PasswordResetRequest, "es")]
    [InlineData(EmailTemplateKey.PasswordChanged, "en")]
    [InlineData(EmailTemplateKey.PasswordChanged, "es")]
    [InlineData(EmailTemplateKey.PasswordResetCancelledEmailChange, "en")]
    [InlineData(EmailTemplateKey.PasswordResetCancelledEmailChange, "es")]
    [InlineData(EmailTemplateKey.EmailChangeVerifyNew, "en")]
    [InlineData(EmailTemplateKey.EmailChangeVerifyNew, "es")]
    [InlineData(EmailTemplateKey.EmailChangeRevokeOld, "en")]
    [InlineData(EmailTemplateKey.EmailChangeRevokeOld, "es")]
    [InlineData(EmailTemplateKey.EmailChangeConfirmed, "en")]
    [InlineData(EmailTemplateKey.EmailChangeConfirmed, "es")]
    [InlineData(EmailTemplateKey.EmailChangeConfirmedToOld, "en")]
    [InlineData(EmailTemplateKey.EmailChangeConfirmedToOld, "es")]
    [InlineData(EmailTemplateKey.EmailChangeRevokeNotificationToOld, "en")]
    [InlineData(EmailTemplateKey.EmailChangeRevokeNotificationToOld, "es")]
    [InlineData(EmailTemplateKey.LockoutUnlock, "en")]
    [InlineData(EmailTemplateKey.LockoutUnlock, "es")]
    public void Renders_all_nine_templates_en_and_es(EmailTemplateKey key, string culture)
    {
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        // Two placeholders is enough for any template (LockoutUnlock and EmailChangeRevokeOld
        // are the only ones with {1}); extras are ignored by String.Format.
        var msg = composer.Compose(key, new CultureInfo(culture), "https://example.invalid/x", "203.0.113.5");

        msg.Subject.Should().NotBeNullOrWhiteSpace();
        msg.BodyText.Should().NotBeNullOrWhiteSpace();
        msg.BodyHtml.Should().NotBeNullOrWhiteSpace();
        msg.Subject.Should().NotContain("{0}").And.NotContain("{1}");
    }

    [Fact]
    public void Strips_cr_lf_in_subject()
    {
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        // PasswordResetRequest subject doesn't include placeholders, but a future template
        // might inject user-supplied data — defense in depth.
        var msg = composer.Compose(EmailTemplateKey.PasswordResetRequest, new CultureInfo("en"),
            "https://example.invalid/\r\nBcc: attacker@evil.invalid");

        msg.Subject.Should().NotContain("\r").And.NotContain("\n");
    }

    [Fact]
    public void Html_encodes_args_in_html_body()
    {
        using var scope = _factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();

        var msg = composer.Compose(EmailTemplateKey.LockoutUnlock, new CultureInfo("en"),
            "https://example.invalid/unlock",
            "<script>alert(1)</script>");

        msg.BodyHtml.Should().NotContain("<script>alert(1)</script>");
        msg.BodyHtml.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void All_resx_keys_present_in_both_cultures()
    {
        // Reads both .resx files via ResourceManager and asserts the key set is identical.
        // Catches a translator dropping a key on a future edit.
        var en = new System.Resources.ResourceManager(
            "ProjectCeres.Resources.Emails",
            typeof(ProjectCeres.Resources.EmailsResource).Assembly);

        // Enumerate via ResourceSet for both cultures.
        var enSet = en.GetResourceSet(new CultureInfo("en"), createIfNotExists: true, tryParents: false);
        var esSet = en.GetResourceSet(new CultureInfo("es"), createIfNotExists: true, tryParents: false);
        enSet.Should().NotBeNull("Emails.en.resx must compile into the assembly");
        esSet.Should().NotBeNull("Emails.es.resx must compile into the assembly");

        var enKeys = enSet!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).OrderBy(k => k).ToList();
        var esKeys = esSet!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).OrderBy(k => k).ToList();

        enKeys.Should().BeEquivalentTo(esKeys);
        enKeys.Should().HaveCount(27, "9 templates × 3 keys each");
    }
}
```

- [ ] **Step 2: Write the failing LanguageResolver test**

Create `ProjectCeres.Tests/Integration/Email/LanguageResolverTests.cs`:

```csharp
using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

[Collection(nameof(IntegrationTestCollection))]
public sealed class LanguageResolverTests
{
    private readonly AuthTestWebApplicationFactory _factory;

    public LanguageResolverTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Falls_back_to_en_when_no_settings_row()
    {
        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ILanguageResolver>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = $"lang-{Guid.NewGuid():N}@example.invalid",
            Email = $"lang-{Guid.NewGuid():N}@example.invalid",
        };
        (await users.CreateAsync(user, "Pa$$w0rd!Test-7K")).Succeeded.Should().BeTrue();

        var culture = await resolver.ResolveForUserAsync(user.Id, CancellationToken.None);

        culture.TwoLetterISOLanguageName.Should().Be("en");
    }

    [Fact]
    public async Task Resolves_es_when_settings_language_is_es()
    {
        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ILanguageResolver>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new ApplicationUser
        {
            UserName = $"lang-{Guid.NewGuid():N}@example.invalid",
            Email = $"lang-{Guid.NewGuid():N}@example.invalid",
        };
        (await users.CreateAsync(user, "Pa$$w0rd!Test-7K")).Succeeded.Should().BeTrue();

        db.Set<Settings>().Add(new Settings
        {
            UserId = user.Id,
            NumberFormat = "1.234,56",
            DateFormat = "dd/MM/yyyy",
            DefaultCurrencyId = 1,
            Language = "es",
        });
        await db.SaveChangesAsync();

        var culture = await resolver.ResolveForUserAsync(user.Id, CancellationToken.None);

        culture.TwoLetterISOLanguageName.Should().Be("es");
    }
}
```

- [ ] **Step 3: Run the failing tests**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~EmailComposerTests|FullyQualifiedName~LanguageResolverTests" --logger "console;verbosity=minimal"`
Expected: build fails — `EmailTemplateKey`, `IEmailComposer`, `ILanguageResolver`, `ProjectCeres.Resources.EmailsResource`, and `Settings.Language` don't exist yet.

- [ ] **Step 4: Add `Language` column to `Settings` entity**

Modify `ProjectCeres/Models/Settings.cs`:

```csharp
using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class Settings : IUserOwned
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string NumberFormat { get; set; } = string.Empty;
    public string DateFormat { get; set; } = string.Empty;
    public int DefaultCurrencyId { get; set; }
    /// <summary>
    /// Day of month (1–31) when monthly cycles start. Drives every monthly view in
    /// the app (Cycle to Date, Spending by Category, Income vs. Avg, Budget periods).
    /// Default 1 = calendar months. For months shorter than the configured day
    /// (e.g., 31 in April), the cycle starts on that month's last day. See BudgetPeriod helper.
    /// </summary>
    public int PeriodStartDay { get; set; } = 1;
    /// <summary>
    /// User's preferred UI language. "en" | "es". Default "en". Drives the culture
    /// used by IStringLocalizer for server-rendered emails and reports. Wired by
    /// Stage 8b; the SPA language toggle PATCHes this column in Stage 9.
    /// </summary>
    public string Language { get; set; } = "en";

    public Currency DefaultCurrency { get; set; } = null!;
}
```

- [ ] **Step 5: Generate the EF migration for the `Language` column**

Run from repo root:

```bash
dotnet ef migrations add AddSettingsLanguageColumn --project ProjectCeres --startup-project ProjectCeres
```

Expected: creates `ProjectCeres/Migrations/<timestamp>_AddSettingsLanguageColumn.cs` with `AddColumn<string>(name: "Language", table: "Settings", defaultValue: "en", maxLength: 8, nullable: false)`. If the generated migration doesn't set a default value, edit the migration's `Up()` method to pass `defaultValue: "en"` so existing rows backfill.

- [ ] **Step 6: Create `EmailTemplateKey` enum**

Create `ProjectCeres/Common/Email/EmailTemplateKey.cs`:

```csharp
namespace ProjectCeres.Common.Email;

public enum EmailTemplateKey
{
    PasswordResetRequest,
    PasswordChanged,
    PasswordResetCancelledEmailChange,
    EmailChangeVerifyNew,
    EmailChangeRevokeOld,
    EmailChangeConfirmed,
    EmailChangeConfirmedToOld,
    EmailChangeRevokeNotificationToOld,
    LockoutUnlock,
}
```

- [ ] **Step 7: Create `EmailsResource` marker type**

Create `ProjectCeres/Resources/EmailsResource.cs`:

```csharp
namespace ProjectCeres.Resources;

/// <summary>Marker type for IStringLocalizer&lt;EmailsResource&gt;. The matching
/// resource files are <c>Emails.en.resx</c> and <c>Emails.es.resx</c> in this
/// folder. .NET's ResourceManager locates them by the type's namespace + name.</summary>
public sealed class EmailsResource { }
```

- [ ] **Step 8: Create `Emails.en.resx` with all 27 keys**

Create `ProjectCeres/Resources/Emails.en.resx`. The XML scaffold (Visual Studio / Rider write this for you; the schema is standard .resx 2.0):

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>

  <data name="PasswordResetRequest.Subject" xml:space="preserve"><value>Reset your Ceres password</value></data>
  <data name="PasswordResetRequest.BodyText" xml:space="preserve"><value>We received a request to reset your password.

Click the link below within 15 minutes to choose a new password:
{0}

If this wasn't you, no action is needed.</value></data>
  <data name="PasswordResetRequest.BodyHtml" xml:space="preserve"><value>&lt;p&gt;We received a request to reset your password.&lt;/p&gt;&lt;p&gt;&lt;a href="{0}"&gt;Reset password&lt;/a&gt;&lt;/p&gt;&lt;p&gt;This link expires in 15 minutes. If this wasn't you, no action is needed.&lt;/p&gt;</value></data>

  <data name="PasswordChanged.Subject" xml:space="preserve"><value>Your Ceres password was changed</value></data>
  <data name="PasswordChanged.BodyText" xml:space="preserve"><value>Your Ceres password was just changed.

If this was you, no further action is needed. All other active sessions have been signed out as a precaution.

If you did not change your password, contact support immediately.</value></data>
  <data name="PasswordChanged.BodyHtml" xml:space="preserve"><value>&lt;p&gt;Your Ceres password was just changed.&lt;/p&gt;&lt;p&gt;If this was you, no further action is needed. All other active sessions have been signed out as a precaution.&lt;/p&gt;&lt;p&gt;If you did not change your password, contact support immediately.&lt;/p&gt;</value></data>

  <data name="PasswordResetCancelledEmailChange.Subject" xml:space="preserve"><value>Pending email change cancelled</value></data>
  <data name="PasswordResetCancelledEmailChange.BodyText" xml:space="preserve"><value>Your Ceres password was just reset. Any pending change to your account email address has been cancelled as a precaution.

Your account email address is unchanged.

If you did not reset your password, contact support immediately.</value></data>
  <data name="PasswordResetCancelledEmailChange.BodyHtml" xml:space="preserve"><value>&lt;p&gt;Your Ceres password was just reset. Any pending change to your account email address has been cancelled as a precaution.&lt;/p&gt;&lt;p&gt;Your account email address is unchanged.&lt;/p&gt;&lt;p&gt;If you did not reset your password, contact support immediately.&lt;/p&gt;</value></data>

  <data name="EmailChangeVerifyNew.Subject" xml:space="preserve"><value>Confirm your new Ceres email address</value></data>
  <data name="EmailChangeVerifyNew.BodyText" xml:space="preserve"><value>Confirm that you want this address to become the email on your Ceres account.

Click the link below within 30 minutes:
{0}

If you did not request this change, ignore this email.</value></data>
  <data name="EmailChangeVerifyNew.BodyHtml" xml:space="preserve"><value>&lt;p&gt;Confirm that you want this address to become the email on your Ceres account.&lt;/p&gt;&lt;p&gt;&lt;a href="{0}"&gt;Confirm new email&lt;/a&gt;&lt;/p&gt;&lt;p&gt;This link expires in 30 minutes. If you did not request this change, ignore this email.&lt;/p&gt;</value></data>

  <data name="EmailChangeRevokeOld.Subject" xml:space="preserve"><value>Email change requested on your Ceres account</value></data>
  <data name="EmailChangeRevokeOld.BodyText" xml:space="preserve"><value>An email change to {0} was requested on your Ceres account.

If this wasn't you, click the link below within 7 days to revoke the change:
{1}

If you did request it, you can ignore this message.</value></data>
  <data name="EmailChangeRevokeOld.BodyHtml" xml:space="preserve"><value>&lt;p&gt;An email change to &lt;strong&gt;{0}&lt;/strong&gt; was requested on your Ceres account.&lt;/p&gt;&lt;p&gt;If this wasn't you, &lt;a href="{1}"&gt;revoke the change&lt;/a&gt; within 7 days.&lt;/p&gt;&lt;p&gt;If you did request it, you can ignore this message.&lt;/p&gt;</value></data>

  <data name="EmailChangeConfirmed.Subject" xml:space="preserve"><value>Ceres email change confirmed</value></data>
  <data name="EmailChangeConfirmed.BodyText" xml:space="preserve"><value>Your Ceres email address was just changed to this address.

All other active sessions have been signed out as a precaution.

If this was NOT you, contact support immediately.</value></data>
  <data name="EmailChangeConfirmed.BodyHtml" xml:space="preserve"><value>&lt;p&gt;Your Ceres email address was just changed to this address.&lt;/p&gt;&lt;p&gt;All other active sessions have been signed out as a precaution.&lt;/p&gt;&lt;p&gt;If this was NOT you, contact support immediately.&lt;/p&gt;</value></data>

  <data name="EmailChangeConfirmedToOld.Subject" xml:space="preserve"><value>Ceres email change confirmed</value></data>
  <data name="EmailChangeConfirmedToOld.BodyText" xml:space="preserve"><value>The Ceres account previously associated with this address ({0}) has been moved to {1}.

All other active sessions have been signed out as a precaution.

If this was NOT you, contact support immediately — you may still be able to recover the account.</value></data>
  <data name="EmailChangeConfirmedToOld.BodyHtml" xml:space="preserve"><value>&lt;p&gt;The Ceres account previously associated with this address (&lt;strong&gt;{0}&lt;/strong&gt;) has been moved to &lt;strong&gt;{1}&lt;/strong&gt;.&lt;/p&gt;&lt;p&gt;All other active sessions have been signed out as a precaution.&lt;/p&gt;&lt;p&gt;If this was NOT you, contact support immediately — you may still be able to recover the account.&lt;/p&gt;</value></data>

  <data name="EmailChangeRevokeNotificationToOld.Subject" xml:space="preserve"><value>Email change cancelled</value></data>
  <data name="EmailChangeRevokeNotificationToOld.BodyText" xml:space="preserve"><value>The pending email change on your Ceres account has been cancelled.

Your account email address is unchanged.

If you did not initiate this cancellation, contact support immediately.</value></data>
  <data name="EmailChangeRevokeNotificationToOld.BodyHtml" xml:space="preserve"><value>&lt;p&gt;The pending email change on your Ceres account has been cancelled.&lt;/p&gt;&lt;p&gt;Your account email address is unchanged.&lt;/p&gt;&lt;p&gt;If you did not initiate this cancellation, contact support immediately.&lt;/p&gt;</value></data>

  <data name="LockoutUnlock.Subject" xml:space="preserve"><value>Ceres account locked</value></data>
  <data name="LockoutUnlock.BodyText" xml:space="preserve"><value>Too many failed sign-in attempts on your Ceres account from IP {1}.

To unlock the account, click the link below within 1 hour:
{0}

If this wasn't you, the link still allows you to unlock — change your password immediately after signing in.</value></data>
  <data name="LockoutUnlock.BodyHtml" xml:space="preserve"><value>&lt;p&gt;Too many failed sign-in attempts on your Ceres account from IP &lt;strong&gt;{1}&lt;/strong&gt;.&lt;/p&gt;&lt;p&gt;&lt;a href="{0}"&gt;Unlock your account&lt;/a&gt;&lt;/p&gt;&lt;p&gt;If this wasn't you, the link still allows you to unlock — change your password immediately after signing in.&lt;/p&gt;</value></data>
</root>
```

- [ ] **Step 9: Create `Emails.es.resx` with the same 27 keys, Spanish translations**

Create `ProjectCeres/Resources/Emails.es.resx`. Use the same XML scaffold. Spanish values:

- `PasswordResetRequest.Subject` = `Restablece tu contraseña de Ceres`
- `PasswordResetRequest.BodyText` = `Hemos recibido una solicitud para restablecer tu contraseña.\n\nHaz clic en el enlace dentro de los próximos 15 minutos para elegir una nueva contraseña:\n{0}\n\nSi no fuiste tú, no es necesario hacer nada.`
- `PasswordResetRequest.BodyHtml` = `<p>Hemos recibido una solicitud para restablecer tu contraseña.</p><p><a href="{0}">Restablecer contraseña</a></p><p>Este enlace caduca en 15 minutos. Si no fuiste tú, no es necesario hacer nada.</p>`

- `PasswordChanged.Subject` = `Se ha cambiado tu contraseña de Ceres`
- `PasswordChanged.BodyText` = `Se acaba de cambiar tu contraseña de Ceres.\n\nSi fuiste tú, no es necesario hacer nada. Todas las demás sesiones activas se han cerrado como medida de precaución.\n\nSi no fuiste tú, contacta con soporte de inmediato.`
- `PasswordChanged.BodyHtml` — analogous `<p>` wrapping of the body text.

- `PasswordResetCancelledEmailChange.Subject` = `Cambio de correo pendiente cancelado`
- `PasswordResetCancelledEmailChange.BodyText` = `Se ha restablecido tu contraseña de Ceres. Cualquier cambio pendiente de la dirección de correo electrónico de tu cuenta ha sido cancelado como medida de precaución.\n\nLa dirección de correo de tu cuenta no ha cambiado.\n\nSi no restableciste la contraseña, contacta con soporte de inmediato.`
- `PasswordResetCancelledEmailChange.BodyHtml` — analogous.

- `EmailChangeVerifyNew.Subject` = `Confirma tu nueva dirección de correo de Ceres`
- `EmailChangeVerifyNew.BodyText` = `Confirma que quieres que esta dirección pase a ser el correo de tu cuenta de Ceres.\n\nHaz clic en el enlace dentro de los próximos 30 minutos:\n{0}\n\nSi no solicitaste este cambio, ignora este mensaje.`
- `EmailChangeVerifyNew.BodyHtml` — analogous.

- `EmailChangeRevokeOld.Subject` = `Solicitud de cambio de correo en tu cuenta de Ceres`
- `EmailChangeRevokeOld.BodyText` = `Se ha solicitado un cambio de correo a {0} en tu cuenta de Ceres.\n\nSi no fuiste tú, haz clic en el enlace dentro de los próximos 7 días para revocar el cambio:\n{1}\n\nSi fuiste tú, puedes ignorar este mensaje.`
- `EmailChangeRevokeOld.BodyHtml` — analogous.

- `EmailChangeConfirmed.Subject` = `Cambio de correo electrónico confirmado en Ceres`
- `EmailChangeConfirmed.BodyText` = `Tu dirección de correo de Ceres se acaba de cambiar a esta dirección.\n\nTodas las demás sesiones activas se han cerrado como medida de precaución.\n\nSi NO fuiste tú, contacta con soporte de inmediato.`
- `EmailChangeConfirmed.BodyHtml` — analogous.

- `EmailChangeConfirmedToOld.Subject` = `Cambio de correo electrónico confirmado en Ceres`
- `EmailChangeConfirmedToOld.BodyText` = `La cuenta de Ceres que antes estaba asociada a esta dirección ({0}) se ha trasladado a {1}.\n\nTodas las demás sesiones activas se han cerrado como medida de precaución.\n\nSi NO fuiste tú, contacta con soporte de inmediato — todavía podrías recuperar la cuenta.`
- `EmailChangeConfirmedToOld.BodyHtml` — analogous.

- `EmailChangeRevokeNotificationToOld.Subject` = `Cambio de correo cancelado`
- `EmailChangeRevokeNotificationToOld.BodyText` = `Se ha cancelado el cambio de correo pendiente en tu cuenta de Ceres.\n\nLa dirección de correo de tu cuenta no ha cambiado.\n\nSi no iniciaste esta cancelación, contacta con soporte de inmediato.`
- `EmailChangeRevokeNotificationToOld.BodyHtml` — analogous.

- `LockoutUnlock.Subject` = `Cuenta de Ceres bloqueada`
- `LockoutUnlock.BodyText` = `Demasiados intentos fallidos de inicio de sesión en tu cuenta de Ceres desde la IP {1}.\n\nPara desbloquear la cuenta, haz clic en el enlace dentro de la próxima hora:\n{0}\n\nSi no fuiste tú, el enlace sigue permitiendo el desbloqueo — cambia tu contraseña inmediatamente después de iniciar sesión.`
- `LockoutUnlock.BodyHtml` — analogous.

- [ ] **Step 10: Create `EmailComposer`**

Create `ProjectCeres/Common/Email/EmailComposer.cs`:

```csharp
using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Localization;
using ProjectCeres.Resources;

namespace ProjectCeres.Common.Email;

public interface IEmailComposer
{
    /// <summary>
    /// Renders the resx-backed template for the given key + culture. Caller is
    /// responsible for setting <see cref="EmailMessage.To"/> via the recipient resolver.
    /// </summary>
    EmailMessage Compose(EmailTemplateKey key, CultureInfo culture, params object[] args);
}

public sealed class EmailComposer : IEmailComposer
{
    private readonly IStringLocalizer<EmailsResource> _localizer;

    public EmailComposer(IStringLocalizer<EmailsResource> localizer) => _localizer = localizer;

    public EmailMessage Compose(EmailTemplateKey key, CultureInfo culture, params object[] args)
    {
        var prior = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            var subjectTpl = _localizer[$"{key}.Subject"].Value;
            var bodyTextTpl = _localizer[$"{key}.BodyText"].Value;
            var bodyHtmlTpl = _localizer[$"{key}.BodyHtml"].Value;

            // HTML body sanitizes interpolations. Subject + plain-text bodies don't
            // HTML-encode (would mangle URLs) but strip CR/LF as a header-injection
            // defense in depth.
            var htmlArgs = args.Select(a => (object)HtmlEncoder.Default.Encode(a?.ToString() ?? "")).ToArray();
            var textArgs = args.Select(a => (object)(a?.ToString() ?? "").Replace("\r", "").Replace("\n", " ")).ToArray();

            var subject = string.Format(culture, subjectTpl, textArgs).Replace("\r", "").Replace("\n", "");
            var bodyText = string.Format(culture, bodyTextTpl, textArgs);
            var bodyHtml = string.Format(culture, bodyHtmlTpl, htmlArgs);

            return new EmailMessage(EmailRecipient.None, subject, bodyHtml, bodyText);
        }
        finally
        {
            CultureInfo.CurrentUICulture = prior;
        }
    }
}
```

- [ ] **Step 11: Create `LanguageResolver`**

Create `ProjectCeres/Common/Email/LanguageResolver.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Email;

public interface ILanguageResolver
{
    Task<CultureInfo> ResolveForUserAsync(Guid userId, CancellationToken ct);
}

public sealed class LanguageResolver : ILanguageResolver
{
    private static readonly CultureInfo En = new("en");
    private static readonly CultureInfo Es = new("es");

    private readonly ApplicationDbContext _db;

    public LanguageResolver(ApplicationDbContext db) => _db = db;

    public async Task<CultureInfo> ResolveForUserAsync(Guid userId, CancellationToken ct)
    {
        // Cross-tenant by design: this is consulted from pre-auth call sites (e.g.
        // password reset triggered before the user is fully signed in). userId always
        // comes from server-side context. Stage 10 architecture test allow-lists this file.
        var lang = await _db.Set<Settings>()
            .IgnoreQueryFilters()
            .Where(s => s.UserId == userId)
            .Select(s => s.Language)
            .FirstOrDefaultAsync(ct);

        return lang switch
        {
            "es" => Es,
            _ => En,
        };
    }
}
```

- [ ] **Step 12: Register localization, composer, language resolver in `Program.cs`**

Modify `ProjectCeres/Program.cs` — add near the existing `IEmailRecipientResolver` registration:

```csharp
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddScoped<IEmailComposer, EmailComposer>();
builder.Services.AddScoped<ILanguageResolver, LanguageResolver>();
```

`AddLocalization` registers `IStringLocalizer<>` and tells `ResourceManager` to look in the `Resources/` folder for `*.<culture>.resx` files.

- [ ] **Step 13: Migrate every `Build*Email` call site in PasswordResetService**

For each `BuildXxxEmail` helper in `ProjectCeres/Common/Authentication/PasswordResetService.cs`:

1. Inject the new dependencies (constructor change at line 47):

```csharp
public PasswordResetService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IEmailService email,
    IEmailComposer composer,
    IEmailRecipientResolver recipients,
    ILanguageResolver languages,
    /* … rest of existing parameters unchanged … */)
{
    _composer = composer;
    _recipients = recipients;
    _languages = languages;
    /* … */
}
```

2. Replace each call site (line 148, 369, 390):

Line 148 (`/request` happy path):
```csharp
var recipient = await _recipients.ResolveAsync(user.Id, ct);
var culture = await _languages.ResolveForUserAsync(user.Id, ct);
var msg = _composer.Compose(EmailTemplateKey.PasswordResetRequest, culture, resetUrl)
    with { To = recipient };
await _email.SendAsync(msg, ct);
```

Line 369 (`PasswordResetCancelledEmailChange` to current address):
```csharp
var recipient = await _recipients.ResolveAsync(user.Id, ct);
var culture = await _languages.ResolveForUserAsync(user.Id, ct);
var msg = _composer.Compose(EmailTemplateKey.PasswordResetCancelledEmailChange, culture)
    with { To = recipient };
await _email.SendAsync(msg, ct);
```

Line 390 (`PasswordChanged` to current address):
```csharp
var recipient = await _recipients.ResolveAsync(user.Id, ct);
var culture = await _languages.ResolveForUserAsync(user.Id, ct);
var msg = _composer.Compose(EmailTemplateKey.PasswordChanged, culture)
    with { To = recipient };
await _email.SendAsync(msg, ct);
```

3. Delete the three `private static EmailMessage Build*Email(...)` helpers (lines 193–228, 407–424, 426–447 approximately).

- [ ] **Step 14: Migrate every `Build*Email` call site in EmailChangeService**

Same pattern as Step 13, applied to `ProjectCeres/Common/Authentication/EmailChangeService.cs`. Five call sites at lines 180, 189, 316, 329, 407:

- Line 180 (`EmailChangeVerifyNew` to NEW address — note: the new email isn't a registered user, so we override): wraps the call site in `EmailRecipient.OverrideForEmailChange(normalized)`:
```csharp
var culture = await _languages.ResolveForUserAsync(userId, ct);
var msg = _composer.Compose(EmailTemplateKey.EmailChangeVerifyNew, culture, verifyUrl)
    with { To = EmailRecipient.OverrideForEmailChange(normalized) };
await _email.SendAsync(msg, ct);
```

- Line 189 (`EmailChangeRevokeOld` to OLD address):
```csharp
var msg = _composer.Compose(EmailTemplateKey.EmailChangeRevokeOld, culture, normalized, revokeUrl)
    with { To = EmailRecipient.OverrideForEmailChange(user.Email!) };
```

- Line 316 (`EmailChangeConfirmed` to new — now `user.Email`): use the resolver (the user now has this email as the verified address):
```csharp
var recipient = await _recipients.ResolveAsync(userId, ct);
var msg = _composer.Compose(EmailTemplateKey.EmailChangeConfirmed, culture) with { To = recipient };
```

- Line 329 (`EmailChangeConfirmedToOld` to old): `OverrideForEmailChange(oldEmail)` with args `oldEmail, match.NewEmail`.

- Line 407 (`EmailChangeRevokeNotificationToOld` to old, which IS `user.Email` because revoke leaves the email unchanged): use the resolver here.

Delete the five `private static EmailMessage Build*Email` helpers (lines 455–554 approximately).

- [ ] **Step 15: Migrate the BuildLockoutEmail call site in LockoutUnlockService**

In `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` line 93:

```csharp
var recipient = await _recipients.ResolveAsync(user.Id, ct);
var culture = await _languages.ResolveForUserAsync(user.Id, ct);
var msg = _composer.Compose(EmailTemplateKey.LockoutUnlock, culture, unlockUrl, ip)
    with { To = recipient };
await _email.SendAsync(msg, ct);
```

Inject the three new dependencies in the constructor (line 42) the same way as PasswordResetService.

Delete `BuildLockoutEmail` (lines 176–202).

- [ ] **Step 16: Apply the migration to the dev DB**

Run: `dotnet ef database update --project ProjectCeres --startup-project ProjectCeres`
Expected: applies the `AddSettingsLanguageColumn` migration. Existing rows get `Language = "en"`.

- [ ] **Step 17: Run the new tests**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~EmailComposerTests|FullyQualifiedName~LanguageResolverTests" --logger "console;verbosity=minimal"`
Expected: all 22 EmailComposer tests + 2 LanguageResolver tests pass.

- [ ] **Step 18: Run the full Authentication suite to catch regressions**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~Authentication" --logger "console;verbosity=minimal"`
Expected: green. Tests that previously asserted against a hard-coded English subject string (e.g. `"Your Project Ceres password was changed"`) will fail — update them to assert against the new "Ceres"-prefixed strings or, better, assert against the resx value the composer would emit (read it via `IStringLocalizer<EmailsResource>` in the test).

- [ ] **Step 19: Commit**

```bash
git -C <repo> add -A
git -C <repo> commit -m "feat(stage-8b): resx-backed EmailComposer + Settings.Language column

Moves all 9 hard-coded email subject/body strings from PasswordResetService,
EmailChangeService, and LockoutUnlockService into Emails.en.resx +
Emails.es.resx (27 keys total — 9 templates × Subject/BodyText/BodyHtml).

Adds IEmailComposer (renders resx + sanitizes interpolations: HTML-encodes
into HTML body, strips CR/LF from subject as header-injection defense) and
ILanguageResolver (reads Settings.Language for the recipient user, falls
back to en).

Adds Settings.Language column ('en' | 'es', default 'en') via a single EF
migration. Stage 9 SPA work will wire the user-facing language toggle.

All 9 Build*Email helpers in the three auth services deleted; services call
the composer. Brand naming changed from 'Project Ceres' to 'Ceres' in every
user-facing email."
```

---

## Task 3 — Sub-stage 8c: Resend SDK integration

**Goal:** Add the Resend NuGet, create `ResendEmailService`, conditionally register it in `Program.cs`. Production startup fails loudly if `Email:Resend:ApiKey` is unbound; dev without a key keeps using `LogOnlyEmailService`.

**Files:**
- Create: `ProjectCeres/Common/Email/EmailOptions.cs`
- Create: `ProjectCeres/Common/Email/ResendEmailService.cs`
- Modify: `ProjectCeres/ProjectCeres.csproj` (add `Resend` package)
- Modify: `ProjectCeres/Program.cs` (conditional registration, production-fail-loud)
- Test: `ProjectCeres.Tests/Integration/Email/ResendEmailServiceTests.cs`

- [ ] **Step 1: Write the failing retry-policy + production-throws tests**

Create `ProjectCeres.Tests/Integration/Email/ResendEmailServiceTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using ProjectCeres.Common.Email;
using Resend;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

public sealed class ResendEmailServiceTests
{
    [Fact]
    public async Task Retries_on_500_three_times_then_throws()
    {
        var resend = new Mock<IResend>();
        var calls = 0;
        resend.Setup(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((_, _) =>
            {
                calls++;
                throw new ResendException(HttpStatusCode.InternalServerError, "transient");
            });

        var svc = new ResendEmailService(
            resend.Object,
            Options.Create(new EmailOptions { Resend = new ResendOptions { FromAddress = "x@example.invalid", FromName = "Ceres" } }),
            NullLogger);

        var msg = new ProjectCeres.Common.Email.EmailMessage(
            EmailRecipient.FromVerifiedUser("test@example.invalid"),
            "Subject", "<p>html</p>", "text");

        await FluentActions.Awaiting(() => svc.SendAsync(msg, CancellationToken.None))
            .Should().ThrowAsync<Exception>();

        calls.Should().Be(3);
    }

    [Fact]
    public async Task Does_not_retry_on_400()
    {
        var resend = new Mock<IResend>();
        var calls = 0;
        resend.Setup(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((_, _) =>
            {
                calls++;
                throw new ResendException(HttpStatusCode.BadRequest, "bad request");
            });

        var svc = new ResendEmailService(
            resend.Object,
            Options.Create(new EmailOptions { Resend = new ResendOptions { FromAddress = "x@example.invalid", FromName = "Ceres" } }),
            NullLogger);

        var msg = new ProjectCeres.Common.Email.EmailMessage(
            EmailRecipient.FromVerifiedUser("test@example.invalid"),
            "Subject", "<p>html</p>", "text");

        await FluentActions.Awaiting(() => svc.SendAsync(msg, CancellationToken.None))
            .Should().ThrowAsync<Exception>();

        calls.Should().Be(1);
    }

    [Fact]
    public void Production_without_api_key_throws_at_startup()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.UseSetting("Email:Resend:ApiKey", "");
            });

        FluentActions.Invoking(() => _ = factory.Services)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Email:Resend:ApiKey is required in Production*");
    }

    private static Microsoft.Extensions.Logging.ILogger<ResendEmailService> NullLogger =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ResendEmailService>.Instance;
}
```

- [ ] **Step 2: Add the Resend NuGet package**

Modify `ProjectCeres/ProjectCeres.csproj` — add to the existing `ItemGroup`:

```xml
<PackageReference Include="Resend" Version="0.1.5" />
```

(Adjust version to the latest stable at implementation time — check `https://www.nuget.org/packages/Resend`.)

Run: `dotnet restore ProjectCeres`
Expected: package restored.

- [ ] **Step 3: Create `EmailOptions` + `ResendOptions`**

Create `ProjectCeres/Common/Email/EmailOptions.cs`:

```csharp
namespace ProjectCeres.Common.Email;

public sealed class EmailOptions
{
    public ResendOptions Resend { get; init; } = new();
}

public sealed class ResendOptions
{
    public string? ApiKey { get; init; }
    public string FromAddress { get; init; } = "onboarding@resend.dev";
    public string FromName { get; init; } = "Ceres";
    public string? WebhookSecret { get; init; }
}
```

- [ ] **Step 4: Create `ResendEmailService`**

Create `ProjectCeres/Common/Email/ResendEmailService.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;

namespace ProjectCeres.Common.Email;

public sealed class ResendEmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly EmailOptions _opts;
    private readonly ILogger<ResendEmailService> _logger;

    private static readonly int[] RetryDelaysMs = { 250, 1000, 4000 };

    public ResendEmailService(IResend resend, IOptions<EmailOptions> opts, ILogger<ResendEmailService> logger)
    {
        _resend = resend;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var req = new Resend.EmailMessage
        {
            From = $"{_opts.Resend.FromName} <{_opts.Resend.FromAddress}>",
            To = message.To.Address,
            Subject = message.Subject,
            HtmlBody = message.BodyHtml,
            TextBody = message.BodyText,
        };

        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _resend.EmailSendAsync(req, ct);
                return;
            }
            catch (ResendException ex) when (IsTransient(ex.StatusCode))
            {
                last = ex;
                _logger.LogWarning(ex, "Resend transient error (attempt {Attempt}/3) status={Status}", attempt + 1, (int)ex.StatusCode);
                if (attempt < 2)
                    await Task.Delay(RetryDelaysMs[attempt], ct);
            }
            catch (ResendException ex)
            {
                _logger.LogError(ex, "Resend permanent error status={Status}", (int)ex.StatusCode);
                throw;
            }
        }
        throw new InvalidOperationException("Resend transient error exhausted retries.", last);
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;
}
```

- [ ] **Step 5: Wire conditional registration in `Program.cs`**

Modify `ProjectCeres/Program.cs` — replace the existing block around line 151:

```csharp
// === Email service registration ===
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

var resendKey = builder.Configuration["Email:Resend:ApiKey"];
if (string.IsNullOrWhiteSpace(resendKey))
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
    builder.Services.AddHttpClient<IResend, ResendClient>(c =>
        c.DefaultRequestHeaders.Authorization = new("Bearer", resendKey));
    builder.Services.AddScoped<IEmailService, ResendEmailService>();
}
```

Delete the existing line `builder.Services.AddSingleton<IEmailService, LogOnlyEmailService>();` and the trailing comment `// Production deliberately has no IEmailService implementation registered.` since both are replaced.

- [ ] **Step 6: Run the new tests**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~ResendEmailServiceTests" --logger "console;verbosity=minimal"`
Expected: 3 tests pass (retry on 500, no retry on 400, production-throws-without-key).

- [ ] **Step 7: Smoke-test against your real Resend API key (dev workflow, not committed)**

```bash
dotnet user-secrets set "Email:Resend:ApiKey" "re_YOUR_KEY" --project ProjectCeres
dotnet run --project ProjectCeres
```

Trigger a password-reset request against your own email via `/api/auth/password-reset/request`. Confirm the email arrives in your inbox (sent from `onboarding@resend.dev`). Body should be plain text + HTML; subject "Reset your Ceres password" (or "Restablece tu contraseña de Ceres" if `Settings.Language = "es"`).

Stop the dev server.

- [ ] **Step 8: Run the full Authentication suite**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~Authentication" --logger "console;verbosity=minimal"`
Expected: green. The test factory does not set `Email:Resend:ApiKey`, so the `LogOnlyEmailService` path stays active in tests.

- [ ] **Step 9: Commit**

```bash
git -C <repo> add -A
git -C <repo> commit -m "feat(stage-8c): ResendEmailService — production IEmailService impl

Adds Resend NuGet package + ResendEmailService wrapping IResend.EmailSendAsync
with 3-attempt exponential backoff (250ms → 1s → 4s) on 5xx + 429; no retry
on 4xx (permanent errors fail loud).

Configuration bound from Email:Resend:* (Email__Resend__ApiKey env var in
production; dotnet user-secrets in dev). Program.cs registers ResendEmailService
when the key is bound; falls back to LogOnlyEmailService in dev without a key;
THROWS InvalidOperationException at startup in Production without a key.

Tests pin retry boundaries (500 retries 3x, 400 retries 0x) and the production
fail-loud behavior."
```

---

## Task 4 — Sub-stage 8d: Per-user + per-IP rate limiting on email-triggering endpoints

**Goal:** Add `EmailByUser` (5/hr/user-or-email) and `EmailByIp` (10/hr/IP) sliding-window policies. Attach them to `/password-reset/request`, `/email-change/request`, `/lockout/unlock/request`, and the webhook endpoint (Task 5). For `/password-reset/request` and `/lockout/unlock/request`, partition by the **normalized email string** from the request payload, not by `UserId` — preserves the Stage 6.16 timing-channel fix (unknown-vs-known users go through the same limiter path).

**Files:**
- Modify: `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs` (add constants)
- Modify: `ProjectCeres/Program.cs` (add the two policies + attach via attributes)
- Modify: `ProjectCeres/Controllers/Api/PasswordResetController.cs` (attach policies)
- Modify: `ProjectCeres/Controllers/Api/EmailChangeController.cs` (attach `EmailByUser`)
- Modify: `ProjectCeres/Controllers/Api/LockoutUnlockController.cs` (attach policies)
- Test: `ProjectCeres.Tests/Integration/Email/EmailRateLimitTests.cs`

- [ ] **Step 1: Write the failing rate-limit tests**

Create `ProjectCeres.Tests/Integration/Email/EmailRateLimitTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using ProjectCeres.Tests.Integration;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

[Collection("RateLimitTests")]
public sealed class EmailRateLimitTests : IClassFixture<RateLimitedAuthTestWebApplicationFactory>
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;

    public EmailRateLimitTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Sixth_password_reset_request_in_same_hour_for_same_email_returns_429()
    {
        await using var factory = _factory.WithFreshRateLimiter();
        var client = factory.CreateClient();
        var email = $"reset-{Guid.NewGuid():N}@example.invalid";

        for (var i = 0; i < 5; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
            ok.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var sixth = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
        sixth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Eleventh_password_reset_request_from_same_ip_returns_429()
    {
        await using var factory = _factory.WithFreshRateLimiter();
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var email = $"ip-reset-{i}-{Guid.NewGuid():N}@example.invalid";
            var ok = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email });
            ok.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var eleventh = await client.PostAsJsonAsync("/api/auth/password-reset/request",
            new { email = $"final-{Guid.NewGuid():N}@example.invalid" });
        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Reset_request_for_unknown_email_and_known_email_have_equal_argon2id_call_count_on_429()
    {
        // Preserves the Stage 6.16 fix: a 429 from EmailByUser must NOT happen before the
        // service runs its constant-time Argon2id work, OR if it does, both branches must
        // hit the same 429 path. We assert by Argon2id call count, not wall-clock timing.
        await using var factory = _factory.WithFreshRateLimiter().WithArgon2idCounter(out var counter);
        var client = factory.CreateClient();

        var knownEmail = $"counter-known-{Guid.NewGuid():N}@example.invalid";
        // Register the known user
        await client.PostAsJsonAsync("/api/auth/register", new { email = knownEmail, password = "Pa$$w0rd!Test-7K" });

        // Burn the EmailByUser bucket for an UNKNOWN email
        var unknownEmail = $"counter-unknown-{Guid.NewGuid():N}@example.invalid";
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email = unknownEmail });

        counter.Reset();
        var unknown429 = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email = unknownEmail });
        var unknownCount = counter.Count;
        unknown429.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Burn the EmailByUser bucket for the KNOWN email
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email = knownEmail });

        counter.Reset();
        var known429 = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email = knownEmail });
        var knownCount = counter.Count;
        known429.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        knownCount.Should().Be(unknownCount,
            "rate-limit denial must not introduce an Argon2id-call-count timing channel " +
            "between unknown and known emails — Stage 6.16 preservation.");
    }
}
```

- [ ] **Step 2: Run the failing tests**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~EmailRateLimitTests" --logger "console;verbosity=minimal"`
Expected: tests fail because no `EmailByUser` / `EmailByIp` policy exists yet.

- [ ] **Step 3: Add policy-name constants**

Modify `ProjectCeres/Common/Authentication/AuthRateLimitPolicies.cs`:

```csharp
public const string EmailByUser = "email-by-user";
public const string EmailByIp = "email-by-ip";
```

- [ ] **Step 4: Add the two policies to `Program.cs`**

In `ProjectCeres/Program.cs`, inside `AddRateLimiter(options => { ... })`, add after the existing `AuthReauthByUser` policy (around line 324):

```csharp
options.AddPolicy(AuthRateLimitPolicies.EmailByUser, httpContext =>
{
    // Partition by NORMALIZED EMAIL from the request body when present (so unknown
    // and known emails go through the same limiter path — Stage 6.16 preservation),
    // falling back to UserId for authenticated callers (email-change/request) and
    // finally to IP for fully anonymous calls.
    var key = TryReadEmailFromBody(httpContext)
              ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
              ?? httpContext.Connection.RemoteIpAddress?.ToString()
              ?? "anonymous-email";
    return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
    {
        PermitLimit = 5,
        Window = TimeSpan.FromMinutes(60),
        SegmentsPerWindow = 6,
        QueueLimit = 0,
    });
});

options.AddPolicy(AuthRateLimitPolicies.EmailByIp, httpContext =>
{
    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
    {
        PermitLimit = 10,
        Window = TimeSpan.FromMinutes(60),
        SegmentsPerWindow = 6,
        QueueLimit = 0,
    });
});
```

Then add a private static helper at the bottom of `Program.cs` (next to `TotpByUserPartitioner`):

```csharp
internal static class EmailPartitionHelpers
{
    /// <summary>
    /// Reads the lowercase "email" property from the request body for limiter partitioning.
    /// Returns null if the body has no email (e.g. authenticated /email-change/request).
    /// Body buffering is required — the rate limiter runs before model binding.
    /// </summary>
    public static string? TryReadEmailFromBody(HttpContext ctx)
    {
        if (ctx.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) != true)
            return null;

        ctx.Request.EnableBuffering();
        ctx.Request.Body.Position = 0;
        using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
        var body = reader.ReadToEnd();
        ctx.Request.Body.Position = 0;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("email", out var emailEl) && emailEl.ValueKind == JsonValueKind.String)
                return emailEl.GetString()?.Trim().ToLowerInvariant();
        }
        catch (JsonException) { /* malformed body — let the model binder produce the 400 */ }

        return null;
    }
}
```

(Make `TryReadEmailFromBody` visible in the lambda scope by adjusting the policy lambdas to call `EmailPartitionHelpers.TryReadEmailFromBody(httpContext)`.)

- [ ] **Step 5: Attach the policies to the three controllers**

Modify `ProjectCeres/Controllers/Api/PasswordResetController.cs` line 19 — the existing `[HttpPost("request"), AllowAnonymous, PreAuthCallSite("PasswordReset.RequestReset")]` becomes:

```csharp
[HttpPost("request"), AllowAnonymous, PreAuthCallSite("PasswordReset.RequestReset"),
 EnableRateLimiting(AuthRateLimitPolicies.EmailByUser),
 EnableRateLimiting(AuthRateLimitPolicies.EmailByIp)]
```

Modify `ProjectCeres/Controllers/Api/EmailChangeController.cs` — add `[EnableRateLimiting(AuthRateLimitPolicies.EmailByUser)]` to the `[HttpPost("request")]` action (the auth-gated path uses the existing `AuthReauthByUser` policy too; here `EmailByUser` partitions on the authenticated UserId, which is the right key for this endpoint).

Modify `ProjectCeres/Controllers/Api/LockoutUnlockController.cs` — add both `EmailByUser` and `EmailByIp` to the `/request` action.

(`EnableRateLimiting` can be applied multiple times per action; ASP.NET applies the limiters in registration order.)

- [ ] **Step 6: Run the rate-limit tests**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~EmailRateLimitTests" --logger "console;verbosity=minimal"`
Expected: 3 tests pass.

- [ ] **Step 7: Run the existing Authentication suite to catch regressions**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~Authentication" --logger "console;verbosity=minimal"`
Expected: green. The existing `PasswordResetRequestTests` may hit the new EmailByUser limiter if it sends >5 requests for the same email — those tests need a fresh limiter (`.WithFreshRateLimiter()` from `RateLimitedAuthTestWebApplicationFactory`) or unique emails per iteration. Fix any test that loops on the same email without resetting.

- [ ] **Step 8: Commit**

```bash
git -C <repo> add -A
git -C <repo> commit -m "feat(stage-8d): per-user + per-IP rate limits on email-triggering endpoints

Adds EmailByUser (5/hr) and EmailByIp (10/hr) sliding-window policies. Attached
to /api/auth/password-reset/request, /api/auth/email-change/request, and
/api/auth/lockout/unlock/request. For unauthenticated email-triggering paths,
EmailByUser partitions by the NORMALIZED EMAIL from the request body — not by
UserId — so unknown-vs-known users go through the same limiter path and the
Stage 6.16 Argon2id-call-count timing-channel fix is preserved.

Pinned by a regression test that asserts equal Argon2id call counts on the
429 path for known vs unknown emails."
```

---

## Task 5 — Sub-stage 8e: ResendWebhookController + EmailDeliveryEvent

**Goal:** Accept Resend's Svix-signed webhook events. Persist each event to a new `EmailDeliveryEvent` table. On `email.bounced`, flip `ApplicationUser.EmailConfirmed = false` for the matching address.

**Files:**
- Create: `ProjectCeres/Common/Email/EmailDeliveryEvent.cs`
- Create: `ProjectCeres/Common/Email/ResendSignatureVerifier.cs`
- Create: `ProjectCeres/Controllers/Api/ResendWebhookController.cs`
- Create: `ProjectCeres/Migrations/YYYYMMDDHHMMSS_AddEmailDeliveryEvent.cs` (generated)
- Modify: `ProjectCeres/Data/ApplicationDbContext.cs` (DbSet + index)
- Modify: `ProjectCeres/Program.cs` (register signature verifier)
- Test: `ProjectCeres.Tests/Integration/Email/ResendWebhookTests.cs`

- [ ] **Step 1: Write the failing webhook tests**

Create `ProjectCeres.Tests/Integration/Email/ResendWebhookTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ResendWebhookTests
{
    private readonly AuthTestWebApplicationFactory _factory;
    private const string TestWebhookSecret = "whsec_test_0123456789abcdef0123456789abcdef";

    public ResendWebhookTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Rejects_bad_signature_with_401()
    {
        await using var factory = _factory.WithWebhookSecret(TestWebhookSecret);
        var client = factory.CreateClient();

        var body = """{"type":"email.delivered","data":{"email_id":"00000000-0000-0000-0000-000000000000","to":["x@example.invalid"]}}""";
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/internal/email-webhook/resend")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Svix-Id", "msg_test_1");
        req.Headers.Add("Svix-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        req.Headers.Add("Svix-Signature", "v1,WRONG_SIGNATURE_BASE64==");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Accepts_valid_signature_and_records_event()
    {
        await using var factory = _factory.WithWebhookSecret(TestWebhookSecret);
        var client = factory.CreateClient();

        var body = """{"type":"email.delivered","data":{"email_id":"11111111-1111-1111-1111-111111111111","to":["delivered-test@example.invalid"]}}""";
        var signed = SignSvix(body, TestWebhookSecret, out var id, out var ts);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/internal/email-webhook/resend")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Svix-Id", id);
        req.Headers.Add("Svix-Timestamp", ts);
        req.Headers.Add("Svix-Signature", $"v1,{signed}");

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rec = await db.Set<EmailDeliveryEvent>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.EmailAddress == "delivered-test@example.invalid");
        rec.Should().NotBeNull();
        rec!.Type.Should().Be("email.delivered");
    }

    [Fact]
    public async Task Bounced_event_flips_EmailConfirmed_to_false()
    {
        await using var factory = _factory.WithWebhookSecret(TestWebhookSecret);
        var client = factory.CreateClient();

        // Seed a user with EmailConfirmed = true
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = "bounce-test@example.invalid",
                Email = "bounce-test@example.invalid",
                EmailConfirmed = true,
            };
            (await users.CreateAsync(user, "Pa$$w0rd!Test-7K")).Succeeded.Should().BeTrue();
        }

        var body = """{"type":"email.bounced","data":{"email_id":"22222222-2222-2222-2222-222222222222","to":["bounce-test@example.invalid"]}}""";
        var signed = SignSvix(body, TestWebhookSecret, out var id, out var ts);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/internal/email-webhook/resend")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Svix-Id", id);
        req.Headers.Add("Svix-Timestamp", ts);
        req.Headers.Add("Svix-Signature", $"v1,{signed}");

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.IgnoreQueryFilters()
                .FirstAsync(u => u.NormalizedEmail == "BOUNCE-TEST@EXAMPLE.INVALID");
            user.EmailConfirmed.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Timestamp_more_than_5_minutes_old_returns_401()
    {
        await using var factory = _factory.WithWebhookSecret(TestWebhookSecret);
        var client = factory.CreateClient();

        var body = """{"type":"email.delivered","data":{"email_id":"33333333-3333-3333-3333-333333333333","to":["x@example.invalid"]}}""";
        var oldTs = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var id = "msg_test_old";
        var signed = ComputeSvixHmac(id, oldTs, body, TestWebhookSecret);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/internal/email-webhook/resend")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Svix-Id", id);
        req.Headers.Add("Svix-Timestamp", oldTs);
        req.Headers.Add("Svix-Signature", $"v1,{signed}");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string SignSvix(string body, string secret, out string id, out string ts)
    {
        id = "msg_test_" + Guid.NewGuid().ToString("N");
        ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return ComputeSvixHmac(id, ts, body, secret);
    }

    private static string ComputeSvixHmac(string id, string ts, string body, string secret)
    {
        // Svix signing secret format: "whsec_<base64>". Strip prefix, decode base64.
        var keyB64 = secret.StartsWith("whsec_") ? secret["whsec_".Length..] : secret;
        var key = Convert.FromBase64String(keyB64.PadRight((keyB64.Length + 3) / 4 * 4, '='));
        using var hmac = new HMACSHA256(key);
        var toSign = Encoding.UTF8.GetBytes($"{id}.{ts}.{body}");
        return Convert.ToBase64String(hmac.ComputeHash(toSign));
    }
}
```

Add a `WithWebhookSecret` helper to `AuthTestWebApplicationFactory` in `ProjectCeres.Tests/Integration/WafCollection.cs`:

```csharp
public WebApplicationFactory<Program> WithWebhookSecret(string secret) =>
    this.WithWebHostBuilder(builder =>
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Resend:WebhookSecret"] = secret,
            })));
```

- [ ] **Step 2: Run the failing tests**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~ResendWebhookTests" --logger "console;verbosity=minimal"`
Expected: build fails — `EmailDeliveryEvent`, `ResendWebhookController`, `WithWebhookSecret` don't exist yet.

- [ ] **Step 3: Create `EmailDeliveryEvent`**

Create `ProjectCeres/Common/Email/EmailDeliveryEvent.cs`:

```csharp
using ProjectCeres.Common;

namespace ProjectCeres.Common.Email;

public sealed class EmailDeliveryEvent : IUserOwned
{
    public Guid Id { get; init; }
    public Guid? UserId { get; init; }
    public string MessageId { get; init; } = "";
    public string Type { get; init; } = "";
    public string EmailAddress { get; init; } = "";
    public string Payload { get; init; } = "";
    public DateTimeOffset OccurredAt { get; init; }
}
```

`IUserOwned` requires `UserId` — the interface allows nullable per `FailedLoginAttempt`'s precedent. Verify the interface signature in `ProjectCeres/Common/IUserOwned.cs`; if `UserId` is non-nullable, define `Guid UserId { get; set; }` on the entity and set `UserId = Guid.Empty` when the email address isn't associated with a known user (and adjust the spec — but `FailedLoginAttempt.UserId` is nullable, so the nullable pattern is the established one).

- [ ] **Step 4: Register the entity in `ApplicationDbContext`**

Modify `ProjectCeres/Data/ApplicationDbContext.cs` — add a `DbSet<EmailDeliveryEvent>` and an index in `OnModelCreating`:

```csharp
public DbSet<EmailDeliveryEvent> EmailDeliveryEvents => Set<EmailDeliveryEvent>();

// inside OnModelCreating:
modelBuilder.Entity<EmailDeliveryEvent>(b =>
{
    b.HasKey(e => e.Id);
    b.Property(e => e.Payload).HasColumnType("jsonb");
    b.HasIndex(e => new { e.EmailAddress, e.OccurredAt }).IsDescending(false, true);
});
```

- [ ] **Step 5: Generate the EF migration**

Run: `dotnet ef migrations add AddEmailDeliveryEvent --project ProjectCeres --startup-project ProjectCeres`
Expected: creates `<timestamp>_AddEmailDeliveryEvent.cs` with `CreateTable("EmailDeliveryEvents", …)` and the index.

- [ ] **Step 6: Create `ResendSignatureVerifier`**

Create `ProjectCeres/Common/Email/ResendSignatureVerifier.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace ProjectCeres.Common.Email;

public interface IResendSignatureVerifier
{
    bool Verify(string svixId, string svixTimestamp, string rawBody, string signatureHeader, string secret);
}

public sealed class ResendSignatureVerifier : IResendSignatureVerifier
{
    private const int ToleranceSeconds = 300; // 5 minutes

    public bool Verify(string svixId, string svixTimestamp, string rawBody, string signatureHeader, string secret)
    {
        if (!long.TryParse(svixTimestamp, out var ts)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > ToleranceSeconds) return false;

        // Strip whsec_ prefix and base64-decode the secret.
        var keyB64 = secret.StartsWith("whsec_") ? secret["whsec_".Length..] : secret;
        byte[] key;
        try { key = Convert.FromBase64String(keyB64.PadRight((keyB64.Length + 3) / 4 * 4, '=')); }
        catch (FormatException) { return false; }

        using var hmac = new HMACSHA256(key);
        var toSign = Encoding.UTF8.GetBytes($"{svixId}.{svixTimestamp}.{rawBody}");
        var expected = hmac.ComputeHash(toSign);

        // Signature header is space-separated list of "v1,<base64>" entries.
        foreach (var entry in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(',', 2);
            if (parts.Length != 2 || parts[0] != "v1") continue;
            byte[] provided;
            try { provided = Convert.FromBase64String(parts[1]); }
            catch (FormatException) { continue; }
            if (CryptographicOperations.FixedTimeEquals(expected, provided)) return true;
        }
        return false;
    }
}
```

Register in `Program.cs`: `builder.Services.AddSingleton<IResendSignatureVerifier, ResendSignatureVerifier>();`

- [ ] **Step 7: Create `ResendWebhookController`**

Create `ProjectCeres/Controllers/Api/ResendWebhookController.cs`:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/internal/email-webhook")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting(AuthRateLimitPolicies.EmailByIp)]
public sealed class ResendWebhookController : ControllerBase
{
    private readonly IResendSignatureVerifier _verifier;
    private readonly IOptions<EmailOptions> _opts;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ResendWebhookController> _logger;

    public ResendWebhookController(
        IResendSignatureVerifier verifier,
        IOptions<EmailOptions> opts,
        ApplicationDbContext db,
        ILogger<ResendWebhookController> logger)
    {
        _verifier = verifier;
        _opts = opts;
        _db = db;
        _logger = logger;
    }

    [HttpPost("resend")]
    [PreAuthCallSite("ResendWebhook.Receive")]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // Buffer the body — rate limiter ran first; we need to read it raw for HMAC.
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var secret = _opts.Value.Resend.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogError("Resend webhook received but Email:Resend:WebhookSecret is not configured.");
            return Unauthorized();
        }

        var svixId = Request.Headers["Svix-Id"].ToString();
        var svixTs = Request.Headers["Svix-Timestamp"].ToString();
        var svixSig = Request.Headers["Svix-Signature"].ToString();
        if (string.IsNullOrEmpty(svixId) || string.IsNullOrEmpty(svixTs) || string.IsNullOrEmpty(svixSig))
            return Unauthorized();
        if (!_verifier.Verify(svixId, svixTs, body, svixSig, secret))
            return Unauthorized();

        JsonElement root;
        try { using var doc = JsonDocument.Parse(body); root = doc.RootElement.Clone(); }
        catch (JsonException) { return BadRequest(); }

        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return BadRequest();
        var type = typeEl.GetString()!;

        string? emailAddress = null;
        string? messageId = null;
        if (root.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("to", out var toEl) && toEl.ValueKind == JsonValueKind.Array && toEl.GetArrayLength() > 0)
                emailAddress = toEl[0].GetString();
            if (data.TryGetProperty("email_id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                messageId = idEl.GetString();
        }
        emailAddress ??= "";
        messageId ??= "";

        Guid? userId = null;
        if (!string.IsNullOrEmpty(emailAddress))
        {
            // Cross-tenant lookup by normalized email — pre-auth call site by attribute.
            var normalized = emailAddress.Trim().ToUpperInvariant();
            userId = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.NormalizedEmail == normalized)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(ct);
        }

        _db.Set<EmailDeliveryEvent>().Add(new EmailDeliveryEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MessageId = messageId,
            Type = type,
            EmailAddress = emailAddress,
            Payload = body,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        if (type == "email.bounced" && !string.IsNullOrEmpty(emailAddress))
        {
            var normalized = emailAddress.Trim().ToUpperInvariant();
            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
            if (user is not null)
            {
                user.EmailConfirmed = false;
            }
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
```

- [ ] **Step 8: Apply the migration to the dev DB**

Run: `dotnet ef database update --project ProjectCeres --startup-project ProjectCeres`

- [ ] **Step 9: Run the webhook tests**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~ResendWebhookTests" --logger "console;verbosity=minimal"`
Expected: all 4 tests pass.

- [ ] **Step 10: Run the full suite**

Run: `dotnet test ProjectCeres.Tests --logger "console;verbosity=minimal"`
Expected: full green.

- [ ] **Step 11: Commit**

```bash
git -C <repo> add -A
git -C <repo> commit -m "feat(stage-8e): Resend delivery webhook + EmailDeliveryEvent

Adds POST /api/internal/email-webhook/resend (anonymous, Svix-signature-gated).
HMAC-SHA256 verification with ±5 minute timestamp tolerance, constant-time
comparison via CryptographicOperations.FixedTimeEquals.

Persists every event to a new EmailDeliveryEvent table (IUserOwned, nullable
UserId, jsonb Payload, indexed on (EmailAddress, OccurredAt DESC)).

On email.bounced, flips ApplicationUser.EmailConfirmed = false for the matching
NormalizedEmail.

EmailByIp rate limit attached (10/hr/IP) as a backstop on the unauthenticated
endpoint."
```

---

## Task 6 — Sub-stage 8f: DNS runbook + roadmap close-out

**Goal:** Ship `docs/runbooks/email-dns-setup.md` so Stage 16 has an executable procedure for publishing SPF/DKIM/DMARC on the registered sending domain. Update roadmap and planning docs to reflect Stage 8 close.

**Files:**
- Create: `docs/runbooks/email-dns-setup.md`
- Modify: `docs/roadmap-phase-three.md` (Stage 8 checklist resolved/deferred, Stage 16 gets new line)
- Modify: `docs/planning-phase3.md` (Batch 3d row ✅; Localization email plumbing ✅)
- Modify: `docs/security-model.md` (cross-link runbook from § Email Security Rules)
- Modify: `docs/models.md` (add `EmailDeliveryEvent`)

- [ ] **Step 1: Create the DNS runbook**

Create `docs/runbooks/email-dns-setup.md`:

```markdown
# Email DNS Setup Runbook — Resend + SPF/DKIM/DMARC

> **When to execute:** as part of Stage 16 (hosting + ops), once a sending domain
> for Ceres has been registered and the Resend production account has been linked
> to that domain. Until then, sending happens from `onboarding@resend.dev`.
>
> **Pre-requisites:** registered domain (e.g. `ceres.example`), DNS zone with
> ability to add TXT and CNAME records, Resend account with admin access, the
> domain added to Resend → Domains.
>
> **Owner:** project maintainer (this is a one-time setup; rotation procedure
> below is recurring).

## Step 1 — Verify the domain in Resend

1. Resend dashboard → Domains → Add Domain → enter `ceres.example`.
2. Resend displays three records to publish:
   - One **CNAME** for DKIM signing (selector subdomain).
   - One **TXT** for SPF.
   - (Optional, recommended) One **TXT** for tracking subdomain.
3. Publish each record on the domain's DNS zone exactly as Resend specifies.
4. Click "Verify" in Resend. Wait for green-checkmark on all three records.

## Step 2 — Publish DMARC at p=none

Add a TXT record at `_dmarc.ceres.example`:

```
v=DMARC1; p=none; rua=mailto:dmarc@ceres.example; aspf=s; adkim=s
```

Set up `dmarc@ceres.example` as a real mailbox (or alias). DMARC aggregate
reports land here once a day from major mailbox providers.

## Step 3 — Verify with external tools

```bash
dig TXT ceres.example +short             # should show v=spf1 include:_spf.resend.com -all
dig CNAME <selector>._domainkey.ceres.example +short   # should show resend DKIM CNAME
dig TXT _dmarc.ceres.example +short      # should show v=DMARC1 p=none ...
```

Also run https://mxtoolbox.com/dmarc.aspx and https://mxtoolbox.com/dkim.aspx
for an external check.

## Step 4 — Advance DMARC policy

After 30 days of aggregate reports showing no legitimate Resend-signed mail
failing DMARC:

```
v=DMARC1; p=quarantine; pct=25; rua=mailto:dmarc@ceres.example; aspf=s; adkim=s
```

After another 30 days clean:

```
v=DMARC1; p=quarantine; pct=100; rua=mailto:dmarc@ceres.example; aspf=s; adkim=s
```

After another 30 days clean:

```
v=DMARC1; p=reject; rua=mailto:dmarc@ceres.example; aspf=s; adkim=s
```

Per `security-model.md § Layer 1`, total ramp is ~90 days.

## Step 5 — MTA-STS + TLS-RPT (optional, hardening)

Publish a policy file at `https://mta-sts.ceres.example/.well-known/mta-sts.txt`:

```
version: STSv1
mode: enforce
mx: feedback-smtp.eu-west-1.amazonses.com
mx: *.resend.com
max_age: 86400
```

(Adjust `mx:` to Resend's documented MX endpoints at the time you publish.)

Add `_mta-sts.ceres.example` TXT:

```
v=STSv1; id=20260101T000000Z
```

Add `_smtp._tls.ceres.example` TXT:

```
v=TLSRPTv1; rua=mailto:tlsrpt@ceres.example
```

## Step 6 — Configure the production sender

Once the domain is verified in Resend:

```
export Email__Resend__FromAddress="noreply@ceres.example"
export Email__Resend__FromName="Ceres"
```

Redeploy. Verify by triggering a password-reset request to your own email.
`From:` header should now show `Ceres <noreply@ceres.example>`.

## Recurring — DKIM rotation (≥ every 6 months)

Resend supports dual-selector rotation. Per `security-model.md § Layer 1`:

1. Resend dashboard → Domains → Rotate DKIM. New selector CNAME shown.
2. Publish the new selector CNAME alongside the existing one.
3. In Resend, switch active signing to the new selector.
4. Wait 48 hours for DNS TTL.
5. Remove the old selector CNAME from DNS.
6. Update this runbook with the rotation date.

## Rollback

If DMARC `p=reject` causes legitimate mail to be rejected (third-party tools
sending on behalf of the domain that weren't accounted for), set back to
`p=quarantine; pct=25` immediately. Investigate via aggregate reports before
re-advancing.
```

- [ ] **Step 2: Update `roadmap-phase-three.md` Stage 8 checklist**

Modify `docs/roadmap-phase-three.md` — for each checklist item under Stage 8:

- Items proved by tests in Tasks 1–5 → mark `[x]`.
- Items requiring DNS publication (Stage 8.3 sub-items: SPF / DKIM / DMARC / aggregate reports / ramp / dual-selector rotation) → mark `[~]` with the note "Runbook shipped at `docs/runbooks/email-dns-setup.md`; publish on the registered domain in Stage 16."
- Items for templates that don't fire yet (registration confirmation, TOTP enrolled/disabled, backup-codes-regenerated, new-session alert, GDPR-export, account-erasure) → leave `[ ]` with the note "Deferred to Stage 9 / 10 / 12 / 13 — wire call-site at that stage."

Also under Stage 16 (`## Stage 16 — Hosting + ops`), add a new checklist line:

```
- [ ] Publish SPF / DKIM / DMARC records on the registered sending domain per `docs/runbooks/email-dns-setup.md`. Advance DMARC `p=none → p=quarantine → p=reject` over ~90 days.
```

- [ ] **Step 3: Update `planning-phase3.md`**

Modify the Phase 3 Roadmap table → Batch 3d row: change `Pending` to `✅ Shipped 2026-05-14`. The "Lands before 3e" rationale stays as-is.

In the Localization section (around line 125), the bullet "Two translation layers: React SPA uses `react-i18next` + JSON files; Server-side emails and reports use `.resx` files (Emails.en.resx, Emails.es.resx, …) via IStringLocalizer" — append: `Server-side email plumbing shipped 2026-05-14 in Stage 8b (9 templates × EN+ES).`

- [ ] **Step 4: Update `security-model.md`**

Modify `docs/security-model.md § Email Security Rules` — add at the bottom of the section:

```markdown
**Operational runbook for DNS configuration:** [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md).

**Compile-time recipient lock (Stage 8a):** `ProjectCeres.Common.Email.EmailMessage.To`
is of type `EmailRecipient`, not `string`. There is no public constructor accepting a
raw string for the To slot. The two legitimate factories are `EmailRecipient.FromVerifiedUser`
(reads `ApplicationUser.Email` by server-context `UserId`) and
`EmailRecipient.OverrideForEmailChange` (used only by `EmailChangeService` to send
notifications to the OLD address during an email change). Pinned by reflection tests
in `ProjectCeres.Tests/Integration/Email/EmailRecipientTests.cs`.
```

- [ ] **Step 5: Update `models.md`**

Modify `docs/models.md` — add a new row in the entity list for `EmailDeliveryEvent`:

```markdown
### `EmailDeliveryEvent`

Records every event Resend's webhook reports about an outgoing email (sent,
delivered, bounced, complained). On `email.bounced`, the receiving handler
flips `ApplicationUser.EmailConfirmed = false` for the affected address.

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `Guid?` | nullable — bounces from addresses with no matching user are still recorded |
| `MessageId` | `string` | Resend's `email_id` |
| `Type` | `string` | `email.sent` / `email.delivered` / `email.bounced` / `email.complained` |
| `EmailAddress` | `string` | the recipient address Resend reported |
| `Payload` | `string` (jsonb) | raw event JSON |
| `OccurredAt` | `DateTimeOffset` | when Ceres recorded the event |

Indexed on `(EmailAddress, OccurredAt DESC)`. Hard-deletes allowed (no soft-delete
retention — these are operational records, not user-owned data).
```

- [ ] **Step 6: Run the full suite one more time + a build**

Run: `dotnet build && dotnet test ProjectCeres.Tests --logger "console;verbosity=minimal"`
Expected: green, no warnings introduced.

- [ ] **Step 7: Commit**

```bash
git -C <repo> add -A
git -C <repo> commit -m "docs(stage-8f): email-DNS runbook + roadmap close-out

Ships docs/runbooks/email-dns-setup.md — executable procedure for publishing
SPF / DKIM / DMARC on the registered sending domain, including dual-selector
DKIM rotation and the 90-day DMARC ramp (p=none → quarantine → reject) per
security-model.md § Layer 1.

Roadmap Stage 8 checklist resolved: test-proved items [x], DNS-publication
items [~] with runbook link, unsent-email-template items [ ] deferred to
Stage 9 / 10 / 12 / 13. Stage 16 gets a new line for executing the runbook
once the domain is registered.

planning-phase3.md Batch 3d row marked ✅. security-model.md § Email Security
Rules cross-links the runbook and documents the compile-time recipient lock.
models.md gets the EmailDeliveryEvent entity row."
```

---

## Self-Review

**Spec coverage:**

- Architecture (4 collaborators): `IEmailService` (Task 3), `IEmailComposer` (Task 2), `IEmailRecipientResolver` (Task 1), `ResendWebhookController` (Task 5) — ✅ all covered.
- Resx layout (9 templates × 3 keys = 27): Task 2 Steps 8–9. ✅
- Resend wiring + secrets + retry policy: Task 3. ✅
- Rate limiting (EmailByUser by-email partition, EmailByIp, Stage 6.16 preservation): Task 4. ✅
- Sanitization rules: Task 2 Step 10 (HTML encode HTML body, strip CR/LF subject). ✅
- `EmailDeliveryEvent` + webhook + bounce-flips-EmailConfirmed: Task 5. ✅
- DNS runbook: Task 6. ✅
- `Settings.Language` migration: Task 2 Steps 4–5. ✅
- Brand naming "Ceres" not "Project Ceres": baked into the resx EN/ES values in Task 2. ✅
- 18 ship-gate tests from the spec → mapped:
  - `EmailComposer_RendersAllNineTemplates_EnAndEs` → Task 2 Step 1 (Theory with 18 InlineData rows). ✅
  - `EmailsResource_AllKeysPresentInBothCultures` → Task 2 Step 1. ✅
  - `EmailComposer_HtmlEncodesArgsInHtmlBody` + `_StripsCrLfInSubject` → Task 2 Step 1. ✅
  - `EmailMessage_NoStringToConstructorExists` → Task 1 Step 1. ✅
  - `EmailRecipientOverride_OnlyCalledByEmailChangeService` → Task 1 Step 1. ✅
  - `LanguageResolver_FallsBackToEn_WhenLanguageColumnUnset` + `LockoutUnlock_ResolvesCultureFromUserSettingsLanguage` → Task 2 Step 2 (renamed `Resolves_es_when_settings_language_is_es`; covers the same property). ✅
  - `ResendEmailService_RetriesOnTransient_NoRetryOn4xx` + `FailsLoudInProductionWithoutApiKey` → Task 3 Step 1. ✅
  - `EmailByUserRateLimit_429sAfter5thSend` + `EmailByIpRateLimit_429sAfter10thSend` + `429s_Across_Branches_Have_Equal_Argon2id_Call_Count` → Task 4 Step 1. ✅
  - `EmailChangeRequest_PartitionsByUserId` → not separately written; the `EmailByUser` policy uses `User.FindFirst(NameIdentifier)` fallback when no body-email exists, and `/email-change/request` is auth-gated so it always has a `User`. This is exercised implicitly by any auth-gated EmailChange test that triggers the limit. **Gap**: I should add a one-line test in Task 4. **Fix inline below.**
  - `ResendWebhook_*` (4 tests) → Task 5 Step 1. ✅

**Placeholder scan:**

```
TBD / TODO / FIXME / XXX → none
"add appropriate" / "handle edge cases" → none
"similar to Task N" → Step references say "same pattern as Step 13", but the
  body is repeated in Step 14 + 15 verbatim, not abbreviated. Acceptable.
```

**Type consistency:** `EmailRecipient`, `IEmailComposer`, `EmailTemplateKey`, `ILanguageResolver`, `IEmailRecipientResolver`, `EmailOptions`, `ResendEmailService`, `IResendSignatureVerifier`, `EmailDeliveryEvent` — same name used everywhere. `EmailMessage`'s record-with-update-clause pattern (`with { To = recipient }`) used consistently across Tasks 2/3. ✅

**Gap fix — add `EmailChangeRequest_PartitionsByUserId` test:**

Add to Task 4 Step 1 (`EmailRateLimitTests.cs`):

```csharp
[Fact]
public async Task Email_change_request_partitions_by_user_id_not_by_email()
{
    // /email-change/request is auth-gated; the EmailByUser limiter falls through to
    // User.NameIdentifier when no body-email is present in the JSON (the request
    // contains "newEmail", not "email"). Two different users hitting /email-change/request
    // for the same `newEmail` should NOT share a limiter bucket.
    await using var factory = _factory.WithFreshRateLimiter();

    // Register two users
    using var setup = factory.Services.CreateScope();
    var users = setup.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var userA = new ApplicationUser { UserName = "a@example.invalid", Email = "a@example.invalid", EmailConfirmed = true };
    var userB = new ApplicationUser { UserName = "b@example.invalid", Email = "b@example.invalid", EmailConfirmed = true };
    (await users.CreateAsync(userA, "Pa$$w0rd!Test-7K")).Succeeded.Should().BeTrue();
    (await users.CreateAsync(userB, "Pa$$w0rd!Test-7K")).Succeeded.Should().BeTrue();

    // (Sign in userA and userB via test cookie auth helpers — pattern matches
    //  EmailChangeRateLimitTests in the existing suite.)
    var clientA = factory.CreateClient(); // sign in as userA
    await SignIn(clientA, "a@example.invalid", "Pa$$w0rd!Test-7K");

    var clientB = factory.CreateClient(); // sign in as userB
    await SignIn(clientB, "b@example.invalid", "Pa$$w0rd!Test-7K");

    // userA: 5 requests targeting the same newEmail = burns their bucket
    for (var i = 0; i < 5; i++)
        await clientA.PostAsJsonAsync("/api/auth/email-change/request", new { newEmail = $"target-{Guid.NewGuid():N}@example.invalid" });

    // userA's 6th should 429
    var a6 = await clientA.PostAsJsonAsync("/api/auth/email-change/request", new { newEmail = $"target-{Guid.NewGuid():N}@example.invalid" });
    a6.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

    // userB's FIRST should succeed — separate bucket
    var b1 = await clientB.PostAsJsonAsync("/api/auth/email-change/request", new { newEmail = $"target-{Guid.NewGuid():N}@example.invalid" });
    b1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
}

private static async Task SignIn(HttpClient client, string email, string password)
{
    var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
    resp.EnsureSuccessStatusCode();
}
```

Fold this into the existing test file rather than re-listing it; spec self-review complete.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-14-stage-8-email-service-plan.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Best fit when each task touches a different layer and there's value in catching mid-task drift before the next sub-stage starts.

**2. Inline Execution** — Execute tasks in this session using `executing-plans`, batch execution with checkpoints for your review.

Which approach?
