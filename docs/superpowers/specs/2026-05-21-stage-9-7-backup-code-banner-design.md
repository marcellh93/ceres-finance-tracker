# Stage 9.7 — Backup-code dashboard banner — Design

**Author:** Claude (orchestrator)
**Date:** 2026-05-21
**Status:** Draft, awaiting user approval
**Parent stage:** [roadmap-phase-three.md § Stage 9.7](../../roadmap-phase-three.md)
**Roadmap line:** `Backup-codes recovery flow — After backup-code login: warning banner on dashboard suggests "Re-enroll TOTP soon. You have N backup codes remaining."` (line 1031)

---

## 1. Problem

Today the dashboard does nothing to nudge a user who has just signed in with a backup code. The signal that the user is one step away from being locked out lives only in the audit log; the user themselves sees a normal dashboard. The roadmap calls for a warning banner on the dashboard whenever the user has consumed a backup code recently. Three pieces are missing:

1. **Server signal — half-wired.** `MeResponse.UsedBackupCodeAtLastLogin` is hard-coded `false` (`AuthController.cs:486`). A comment in that file explicitly defers the real wiring to "Phase 4" — but Phase 4 doesn't exist; this lives in Phase 3 Stage 9.7.
2. **Dashboard banner — absent.** `Dashboard.tsx` has no component reading the flag.
3. **CTA logic — absent.** The user's product decision (2026-05-21) is that a subsequent normal-TOTP login does NOT clear the banner, but it does shift the CTA from "re-enrol your authenticator" to "regenerate your backup codes". No client logic exists for that shift.

## 2. Goals

- Drive the dashboard banner from a real server signal that survives multi-device login (signing in normally on a different device must not silently flip the banner off on the dashboard the user is looking at).
- Banner appears when `usedBackupCodeAtLastLogin === true` OR `backupCodesRemaining ≤ 7`. The two states render different CTAs.
- Dismissible per-pageview; the dismiss is React-local state — no `localStorage`, no server call. Reloading or navigating back to the dashboard re-shows the banner if conditions still apply.
- Bilingual (EN + ES) under a new `dashboard.backupCodeBanner.*` namespace.

## 3. Non-goals

- A reusable `<Alert>` design-system primitive. The recipe at `TotpEnrollStep2BackupCodes.tsx:75-77` (and `BudgetCreate.tsx:118`) is the established inline warning idiom. A primitive can be promoted when a third or fourth caller appears.
- Persisting the dismiss across sessions or pageviews. The user explicitly chose the per-pageview model.
- Changing the threshold dynamically based on user behavior. Threshold is a constant `7` for now.
- Touching the existing hard-coded "Dashboard" heading or localizing other dashboard widgets.

## 4. Decisions locked in by the user (2026-05-21)

| # | Question | Answer |
|---|---|---|
| 1 | When does the banner show? | When `backupCodesRemaining ≤ 7` (regardless of which factor was last used). |
| 2 | Dismissable? | Yes — small X. Per-pageview only. Reappears on reload. |
| 3 | Does a subsequent normal-TOTP login clear it? | No. Banner stays, but the CTA shifts to "regenerate codes" (rather than "re-enrol authenticator"). |

## 5. Server design

### 5.1 Schema change

Add a single nullable boolean column to `UserSession`:

```csharp
// ProjectCeres/Models/UserSession.cs
public sealed class UserSession : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? PersistentTokenHash { get; set; }
    public string IpCreatedAt { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsPersistent { get; set; }
    public bool UsedBackupCodeAtLogin { get; set; }   // NEW
}
```

EF migration: `20260521xxxxxx_AddUsedBackupCodeAtLoginToUserSession`. Adds the column with a `default false` server-side so existing rows backfill cleanly. No index needed — only ever read for one row at a time, keyed by primary key.

`UserSession` is in `UserOwnedTables.All` (verified via the `IUserOwned` interface implementation) and is already under RLS. Adding a non-key column does not affect the policy.

### 5.2 Write site

`AuthController.LoginTotp` already calls `IssueSessionAndCookiesAsync(user, sessionId, rememberMe)` from BOTH the TOTP branch (line 322) and the backup-code branch (line 359). The session row is constructed inside that method (lines 373-382).

We pass a fourth boolean parameter through:

```csharp
private async Task IssueSessionAndCookiesAsync(
    ApplicationUser user,
    Guid sessionId,
    bool rememberMe,
    bool usedBackupCode)   // NEW
{
    ...
    var session = new UserSession
    {
        ...
        IsPersistent = rememberMe,
        UsedBackupCodeAtLogin = usedBackupCode,   // NEW
    };
    ...
}
```

Callers:
- TOTP branch (line 322): `IssueSessionAndCookiesAsync(user, sessionId, rememberMe, usedBackupCode: false)`
- Backup-code branch (line 359): `IssueSessionAndCookiesAsync(user, sessionId, rememberMe, usedBackupCode: true)`

There are no other call sites of `IssueSessionAndCookiesAsync` (verified by grep).

### 5.3 Read site — `AuthController.Me`

Replace the hard-coded `false` and the stale Phase 4 comment with a lookup keyed off the `sid` claim:

```csharp
var sidClaim = User.FindFirst(SessionConstants.SessionIdClaim)?.Value;
var usedBackupCodeAtLastLogin = false;
if (Guid.TryParse(sidClaim, out var sessionId))
{
    usedBackupCodeAtLastLogin = await _db.UserSessions
        .Where(s => s.Id == sessionId && s.UserId == user.Id && s.RevokedAt == null)
        .Select(s => s.UsedBackupCodeAtLogin)
        .FirstOrDefaultAsync(HttpContext.RequestAborted);
}
```

The `&& s.UserId == user.Id` is belt-and-braces — RLS already filters by user, but the explicit filter avoids surprising results if the session-revocation validator ever changes behavior. `FirstOrDefaultAsync` returning `false` on no-match is the right default (no session row means no signal, which means no banner).

### 5.4 Why not the other approaches

We considered three alternatives and chose this one:

- **Boolean on the user row** (`LastLoginUsedBackupCode` on `ApplicationUser`): rejected — multi-device login on phone would silently clear the banner on desktop, contradicting the user's answer #3.
- **Infer from audit log** (read most recent `AuditLogAction.LoginSucceededBackupCode`): rejected — adds an audit-log hot-path read to every dashboard load; audit log is for forensics, not request-path reads.
- **Boolean on `UserSession`** (chosen): per-session signal, naturally correct for multi-device because each session row is independent.

## 6. Client design

### 6.1 Component

New file: `ProjectCeres.Client/src/app/features/security/BackupCodeLoginBanner.tsx`

Lives under `features/security` (not `features/dashboard`) because the concern is security-state, not dashboard-content. Dashboard is the consumer; the banner doesn't depend on dashboard widgets.

Shape:

```tsx
export function BackupCodeLoginBanner() {
  const { user } = useAuth();
  const { t } = useTranslation();
  const [dismissed, setDismissed] = useState(false);

  if (dismissed) return null;
  if (!user) return null;
  if (user.backupCodesRemaining > 7) return null;
  if (!user.twoFactorEnabled) return null;  // banner is irrelevant if MFA is off

  const variant = user.usedBackupCodeAtLastLogin ? 'reenrol' : 'regenerate';
  const ns = `dashboard.backupCodeBanner.${variant}`;

  return (
    <div
      role="status"
      aria-live="polite"
      className="flex items-start gap-3 rounded-md border border-warning/30 bg-warning/10 p-4 text-sm"
    >
      <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" aria-hidden />
      <div className="flex-1 space-y-2">
        <p>
          <Trans
            i18nKey={`${ns}.body`}
            values={{ count: user.backupCodesRemaining }}
            components={{ b: <strong /> }}
          />
        </p>
        <Button asChild variant="link" size="sm" className="h-auto p-0">
          <Link to="/security">{t(`${ns}.cta`)}</Link>
        </Button>
      </div>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="-mt-1 -mr-2 h-7 w-7 shrink-0"
        onClick={() => setDismissed(true)}
        aria-label={t('dashboard.backupCodeBanner.dismiss')}
      >
        <X className="h-4 w-4" />
      </Button>
    </div>
  );
}
```

Why these choices:
- `role="status"` + `aria-live="polite"` — screen readers announce the banner without being interrupting (matches the user's "this is a nudge, not an alert" framing).
- `border-warning/30 bg-warning/10 text-warning` — the exact recipe used at `TotpEnrollStep2BackupCodes.tsx:75-77` and `BudgetCreate.tsx:118`. Verified against `docs/design-system.md` § Semantic tokens (line 96 / 234).
- `variant === 'reenrol' | 'regenerate'` keys the i18n bundle and removes any need for ternaries in the JSX.
- `<Trans>` is required because the body interpolates `<b>{{count}}</b>` — pure `t()` cannot wrap interpolations in tags.
- `<Link to="/security">` — `react-router-dom`'s declarative link. The Security page already exists.
- Local `useState(false)` for dismiss — no `localStorage`. Component remounts (e.g. SPA navigation away and back to `/`) reset the state. Per the user's product decision.
- Early returns ordered cheapest-first (`dismissed`, `!user`, `backupCodesRemaining > 7`, `!twoFactorEnabled`) — the last guard prevents the banner from rendering for a user who turned MFA off but still has a stale `backupCodesRemaining` value in the auth context.

### 6.2 Dashboard wire-up

`ProjectCeres.Client/src/app/pages/Dashboard.tsx` gains one import and renders the banner as the first child of the dashboard container, above the heading. The banner self-suppresses when conditions aren't met, so the rest of the dashboard renders unchanged.

```tsx
import { BackupCodeLoginBanner } from '../features/security/BackupCodeLoginBanner';

export function Dashboard() {
  ...
  return (
    <div className="mx-auto max-w-7xl space-y-6">
      <BackupCodeLoginBanner />
      <h1 ...>Dashboard</h1>
      ...
    </div>
  );
}
```

Above the heading because: this is a security nudge, not dashboard content. Putting it inside the content grid would mix two concerns and would not get screen-reader attention on first page render.

### 6.3 i18n keys

**EN (`ProjectCeres.Client/src/app/i18n/locales/en.json`)** — new top-level namespace:

```json
{
  "dashboard": {
    "backupCodeBanner": {
      "dismiss": "Dismiss",
      "reenrol": {
        "body": "You signed in with a backup code. You have <b>{{count}}</b> code{{count, plural, one {} other {s}}} left. Re-enrol your authenticator app to restore normal sign-in.",
        "cta": "Re-enrol authenticator"
      },
      "regenerate": {
        "body": "You're running low on backup codes — <b>{{count}}</b> left. Generate a new set so you're covered if you lose your device.",
        "cta": "Regenerate backup codes"
      }
    }
  }
}
```

**ES (`ProjectCeres.Client/src/app/i18n/locales/es.json`):**

```json
{
  "dashboard": {
    "backupCodeBanner": {
      "dismiss": "Cerrar",
      "reenrol": {
        "body": "Iniciaste sesión con un código de respaldo. Te quedan <b>{{count}}</b> código{{count, plural, one {} other {s}}}. Vuelve a registrar tu aplicación de autenticación para restablecer el inicio de sesión normal.",
        "cta": "Volver a registrar autenticador"
      },
      "regenerate": {
        "body": "Te quedan pocos códigos de respaldo: <b>{{count}}</b>. Genera un conjunto nuevo para no quedarte sin acceso si pierdes el dispositivo.",
        "cta": "Regenerar códigos de respaldo"
      }
    }
  }
}
```

The `{{count, plural, one {} other {s}}}` form is i18next's ICU-style plural handling — `code` for 1, `codes` for everything else. ES uses the same form because the same noun pluralizes (`código` / `códigos`).

## 7. Testing

### 7.1 Server-side integration tests

New file: `ProjectCeres.Tests/Integration/Authentication/Mfa/BackupCodeLoginSessionFlagTests.cs` — three `[Fact]`s:

1. **`Login_with_backup_code_sets_UsedBackupCodeAtLogin_true_on_session_row`** — register user, enrol TOTP, persist backup codes, log in via `/api/auth/login` → `/api/auth/login/totp` with a backup code. Read `UserSessions` (via admin context); assert the freshly-inserted row has `UsedBackupCodeAtLogin = true`.
2. **`Login_with_TOTP_code_sets_UsedBackupCodeAtLogin_false_on_session_row`** — same setup, but log in with a real TOTP code. Assert the freshly-inserted row has `UsedBackupCodeAtLogin = false`.
3. **`Me_returns_UsedBackupCodeAtLastLogin_for_current_session_only`** — register user, enrol TOTP. Log in once with backup code (session A). Log in again on a "different device" by issuing a separate cookie container against the same WebApplicationFactory using a real TOTP code (session B). Assert: `GET /me` with session A's cookie returns `usedBackupCodeAtLastLogin: true`. `GET /me` with session B's cookie returns `usedBackupCodeAtLastLogin: false`. This is the multi-device-correctness pin that justified picking the per-session column.

### 7.2 Client-side Vitest tests

New file: `ProjectCeres.Client/src/app/features/security/BackupCodeLoginBanner.test.tsx` — four cases:

1. **Renders the re-enrol CTA when `usedBackupCodeAtLastLogin: true, backupCodesRemaining: 5`.** Assert text "Re-enrol authenticator" is in the DOM; the body mentions "5".
2. **Renders the regenerate CTA when `usedBackupCodeAtLastLogin: false, backupCodesRemaining: 3`.** Assert text "Regenerate backup codes" is in the DOM; body mentions "3".
3. **Renders nothing when `backupCodesRemaining: 8`.** Assert `container.firstChild` is `null`.
4. **Dismiss hides the banner for the current render.** Render with banner-showing state, click the dismiss button (`aria-label="Dismiss"`), assert banner disappears. Then re-render the component fresh and assert the banner is back (per-pageview semantics).

Plus a fifth case under the existing `Dashboard.test.tsx` if one exists, or a new minimal one if not: **Dashboard renders BackupCodeLoginBanner as its first child.** (One-line existence test.)

### 7.3 Regression tests not needed

- The existing `MeEndpointTests.cs` already asserts `UsedBackupCodeAtLastLogin.Should().BeFalse();` for the default-state case. After this change that test still passes (because the default user has no backup-code login). No update needed.
- `auth-context.test.tsx` already declares the field in its fixtures. No update needed.

## 8. Verification commands (Definition of Done)

```bash
# Backend
dotnet build
dotnet test --filter "FullyQualifiedName~ProjectCeres.Tests.Integration.Authentication"
# Frontend
pnpm --dir ProjectCeres.Client build
pnpm --dir ProjectCeres.Client test
```

All four must exit 0 before flipping the roadmap line to `[x]`.

## 9. Accessibility

- `role="status"` + `aria-live="polite"`: announces the banner without interrupting screen readers (matches the nudge semantics).
- `<AlertTriangle aria-hidden>`: icon is decorative; the text carries the message.
- `<Button aria-label="Dismiss">`: the X icon's button has a textual label.
- Tab order: visible CTA link → dismiss button. Both are real buttons / links, not divs with click handlers.
- Color contrast: `text-warning` against `bg-warning/10` is sufficient for body copy at 14px+ per design-system.md line 1116 (warning is L=0.770 in light mode, AA-Large only — but our body is paired with `text-foreground`/`text-warning` semantic, and the icon carries the redundant signal).

Open consideration: `docs/design-system.md` line 1118 notes there's no `--warning-foreground` token. The body text here uses default foreground; only the icon and the `<strong>` tag inherit `text-warning`. Verified visually against existing precedents (`TotpEnrollStep2BackupCodes`, `BudgetCreate`).

## 10. Roadmap update

After implementation, the roadmap line at `docs/roadmap-phase-three.md:1031` flips from:

```
- [ ] After backup-code login: warning banner on dashboard suggests "Re-enroll TOTP soon. You have N backup codes remaining."
```

to:

```
- [x] After backup-code login: warning banner on dashboard suggests "Re-enroll TOTP soon. You have N backup codes remaining." — `BackupCodeLoginBanner.tsx` renders at the top of `Dashboard.tsx` when `backupCodesRemaining ≤ 7`. CTA shifts between "Re-enrol authenticator" (when `usedBackupCodeAtLastLogin = true`) and "Regenerate backup codes" (when the user has since logged in normally). Server signal pinned by `BackupCodeLoginSessionFlagTests.cs` (3 [Fact]s); SPA pinned by `BackupCodeLoginBanner.test.tsx` (4 cases). Per-pageview dismiss; reappears on reload. Threshold = 7.
```

The matching wording is preserved verbatim (per the project's "no doc rewrites under cover of a tick" rule); the appended annotation is the audit trail.

## 11. Open questions / follow-ups

None blocking. Two notes for future work:

- **Promote to a primitive at the third caller.** Today the inline `border-warning/30 bg-warning/10` recipe has three callers (TotpEnrollStep2BackupCodes, BudgetCreate, BackupCodeLoginBanner). At a fourth, promote to `<Alert variant="warning">` in `src/components/ui/alert.tsx` and back-port the three call sites in the same commit.
- **`--warning-foreground` token.** When the next warning-banner work lands, add a paired foreground token per `docs/design-system.md` line 1118. Not required by this banner since it uses the default foreground for body copy.

## 12. Spec self-review

- **Placeholders:** none. No TBDs.
- **Internal consistency:** § 5.3 reads `_db.UserSessions` (the DbSet name verified at `AppDbContext.cs:84`). § 6.1 reads `user.usedBackupCodeAtLastLogin` (the field name verified at `auth-context.tsx:11`). The two component variant keys (`reenrol`, `regenerate`) match between §6.1 and §6.3.
- **Scope:** one server column, one server endpoint update, one new component, one Dashboard import, one i18n namespace. Single-PR-sized.
- **Ambiguity:** the "≤ 7" threshold and the "regardless of which factor was last used" semantics are explicit in §4 and §6.1 (the gate `user.backupCodesRemaining > 7`).
