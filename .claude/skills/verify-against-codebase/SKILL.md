---
name: verify-against-codebase
description: >
  Use when reviewing a plan, spec, or piece of code BEFORE dispatching it to subagents
  or committing — to catch project-convention conflicts (wrong HTTP status codes,
  references to model fields that don't exist, hand-rolled UI patterns where a documented
  primitive exists, shadcn API shapes that differ from base-ui, derived-column violations).
  Triggers on phrases like "verify this plan", "check this for project conventions",
  "audit before dispatch", "is this consistent with the codebase". Internally tiered:
  the audit reads only the sections relevant to the diff (backend / frontend / both).
---

# verify-against-codebase

Project-convention pre-flight audit. Run BEFORE the work happens — corrections are cheapest before commitment.

> **History (2026-05-26).** This skill was split into `verify-backend` + `verify-frontend` siblings on 2026-05-17 to avoid running a backend audit on a `.tsx`-only diff (and vice versa). The split paid off in theory but added orchestration overhead: a router pointing at two siblings, each with ~60% duplicate scaffolding, and the agent occasionally invoked the wrong half. The siblings were merged back into this single skill on 2026-05-26 as part of the Phase 1 / 9.5a cleanup. The tiering is now internal — this skill reads only the relevant sections based on what the artifact actually touches.

## Why this skill exists

A plan or spec written from generic defaults will drift from this project's actual conventions. Real instances:

**Backend drift:**
- ASP.NET `Program.cs` configures `InvalidModelStateResponseFactory` to return **422 Unprocessable Entity**, not 400 — but plans written from defaults assume 400.
- The `Account` model is intentionally minimal (no `OpeningBalance`, `OpeningBalanceDate`, `CreatedAt` fields per the project's "no derived columns" rule) — but plans assume those fields exist because they're common in finance apps.
- MFA Stage 6b.1 first draft proposed a homegrown `MfaTicketService` that duplicated ASP.NET Identity's built-in `Identity.TwoFactorUserId` cookie — caught only because the audit forced a read of `Program.cs`.

**Frontend drift:**
- The shadcn `<Badge>` has been extended with `success`/`warning`/`info` variants — but new code keeps hand-rolling `bg-{semantic}/10 text-{semantic}` spans.
- shadcn primitives use `@base-ui/react`, not Radix — `<PopoverTrigger render={...}>` not `asChild`, `<TabsRoot onValueChange={(v, eventDetails) => ...}>` not Radix's signature, `<MenuItem onClick>` not `onSelect`.
- Tabular numerics keep getting rendered as inline `font-mono` spans instead of `<Numeric>` — losing locale-aware formatting and right-alignment.

Each of these caused implementer rework. Catching them before dispatch saves a round trip.

## When to use

Run before:
- Committing to a written plan that touches `.cs` / `.csproj` / `.ts` / `.tsx` / shadcn / Tailwind code (after `writing-plans`, before `subagent-driven-development`).
- Dispatching a long-running subagent.
- Committing a non-trivial change.

Run also when:
- The user says "verify this", "audit this against the codebase", "check this for project conventions".
- A subagent reports `DONE_WITH_CONCERNS` flagging a possible convention mismatch.

Do NOT run for:
- Trivial typo fixes.
- Pure documentation updates.
- Code that already passed a similar check earlier in the session and hasn't changed.

## Internal tiering — read only what the diff touches

Inspect the diff or proposed artifact first. Run only the relevant section(s):

- **Backend signals** (`ProjectCeres/`, `Program.cs`, `*.cs`, `*.csproj`, EF Core, ASP.NET pipeline, HTTP status codes) → run the **Backend pre-flight** (6 steps below).
- **Frontend signals** (`ProjectCeres.Client/`, `*.tsx`, `*.ts`, shadcn, base-ui, Tailwind, design-system primitives) → run the **Frontend pre-flight** (5 steps below).
- **Both signals or no detectable signals (default-safe)** → run both.

## Backend pre-flight (6 steps)

### 1. Project rules — `CLAUDE.md`

Skim every bullet. The "What NOT to Do" section, "Architecture Rules (Non-Obvious)" section, and the "After Completing Any Stage" rule are load-bearing. If the artifact violates any of them, that's a stop-the-line finding.

### 2. API conventions — `docs/api-contract.md`

Confirm:
- Response wrapper shape (DTO vs raw array).
- Success status codes (200 vs 201 with Location).
- Error status codes (**this project: 422 with `ValidationProblemDetails` for ModelState failures**, NOT 400).
- camelCase JSON keys (ASP.NET default).

### 3. ASP.NET pipeline — `ProjectCeres/Program.cs`

Confirm:
- `InvalidModelStateResponseFactory` is set (returns **422**).
- This means: a manual `return ValidationProblem(ModelState)` returns **400** because it bypasses the factory. Use `return UnprocessableEntity(...)` directly with the same JSON shape if you need to add cross-field errors after the auto-validation step.
- DI registrations match what the artifact assumes.

### 4. Affected models — `ProjectCeres/Models/<Name>.cs`

For every model the artifact references, read the actual class. Verify:
- Field names exist exactly as written.
- Field nullability matches.
- Required props for entity creation are all set in seed/test helpers.
- Per project rules, derived values (`Account` balance, `Budget` actual spend, net worth) are NOT stored as columns — don't try to set them.

Common surprise: `Account` has `Id`, `Name`, `AccountTypeId`, `CurrencyId`, `Description?`, `IsActive`, `ExcludeFromSpendable`, `LiabilityRepaymentType?`, `InterestRate?` — and **nothing else**. No `OpeningBalance`, no `OpeningBalanceDate`, no `CreatedAt`.

### 5. Razor vs SPA boundary

Confirm:
- New Razor changes do not reference SPA-only patterns (React hooks in a `.cshtml` file, `useApi` outside a React tree).
- The migration boundary in `docs/planning-phase3-spa-migration.md` is respected.

### 6. Active session memory & open questions

Skim `git log --oneline -20` and check `docs/planning-resolved.md` for decisions that override earlier docs.

## Frontend pre-flight (5 steps)

### 1. Project rules — `CLAUDE.md`

Skim every bullet, with extra attention to the **Frontend Work** section. The design-system-first rule and the post-implementation UX/UI verification checklist are load-bearing.

### 2. shadcn primitive baseline — `ProjectCeres.Client/components.json` + `src/components/ui/<primitive>.tsx`

Confirm:
- `style: base-nova`, `baseColor: zinc`, `cssVariables: true`.
- Project uses `@base-ui/react`, NOT Radix. API differences:
  - `<PopoverTrigger render={<Button>...</Button>}>` instead of Radix `asChild`.
  - `<MenuItem onClick={...}>` instead of Radix `onSelect`.
  - `<Tooltip delay={0}>` instead of Radix `delayDuration`.
  - `<Tabs onValueChange={(value, eventDetails) => ...}>` — second param exists; `(value: TabKey) => void` will fail TS check.
- Existing variants on the primitive being used (e.g. `<Badge>` has `default`, `secondary`, `outline`, `destructive`, `ghost`, `link`, `success`, `warning`, `info`).
- Whether the primitive has been customized (e.g. `<Button>` has project `cursor-pointer` override).

### 3. Design system primitives — `docs/design-system.md`

For every UI pattern the artifact introduces, confirm there isn't already a project primitive:
- Tabular numerics → `<Numeric>`, never inline `font-mono`.
- KPI tiles → `<StatTile>` / `<StatRow>` / `<EquationRow>` / `<Tile>`.
- Status badges → `<Badge variant="success|warning|info">`, never hand-rolled `bg-{x}/10 text-{x}` spans.
- Card error + retry → `<CardError section onRetry>`.
- Toasts → `import { toast } from 'sonner'`, never window.alert or custom DOM banners.
- Chart colors → `chartColors` util returning `var(--chart-N)`, never hex literals.

### 4. Razor vs SPA boundary (frontend side)

Confirm:
- New SPA code does not reference Razor-only patterns (server-rendered ViewBag, MVC RedirectToAction returning a view, hidden `_RequestVerificationToken` inputs).
- The migration boundary in `docs/planning-phase3-spa-migration.md` is respected.

### 5. Active session memory & open questions

Skim `git log --oneline -20` and check `docs/planning-resolved.md`. The `docs/design-system.md` doc itself evolves — confirm tokens / primitives named in the artifact match current state.

## Output format

Report a short list per section that ran. Example mixed-diff output:

```
verify-against-codebase: 12 conventions verified across backend + frontend.
   2 conflicts found:

   1. [Backend, file:line in artifact] Asserts 400; project returns 422.
      → Change to HttpStatusCode.UnprocessableEntity.
   2. [Frontend, file:line in artifact] Uses Radix `asChild` on PopoverTrigger.
      → base-ui uses render={...} prop. See ProjectCeres.Client/src/components/ui/popover.tsx.
```

If no conflicts: `verify-against-codebase: clean. Safe to dispatch.`

## Red flags — STOP before dispatching

**Backend:**
- Any HTTP status code in the artifact that isn't documented in `docs/api-contract.md`.
- Any reference to a model field you didn't verify by reading the model class.
- A plan that says "Returns 400 with ValidationProblemDetails" — that combo doesn't exist in this project.
- An artifact that sets a derived value (`Account.Balance`, `Budget.ActualSpend`, net worth) as a column.

**Frontend:**
- Any inline UI pattern that has a `docs/design-system.md` entry.
- Any shadcn API call (`asChild`, `onSelect`, `delayDuration`) that's a Radix idiom — base-ui differs.
- Any `font-mono` span used for numeric display — should be `<Numeric>`.
- Any hex color literal or hard-coded brand color in a chart — should be `chartColors`.
- Any window.alert / custom DOM toast — should be sonner's `toast()`.

If any red flag is present, fix the artifact before dispatching.

## What this skill does NOT do

- Does not run tests. `dotnet test` / `pnpm test` are separate, scoped per `feedback_never_skip_tests_to_make_them_pass`.
- Does not lint or type-check.
- Does not run accessibility / visual-design audits — those are `web-design-guidelines` and `impeccable`.
- Does not validate spec completeness — that's the spec-reviewer subagent's job.

This skill is a convention pre-flight, nothing more.
