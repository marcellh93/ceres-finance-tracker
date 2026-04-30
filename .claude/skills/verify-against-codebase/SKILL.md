---
name: verify-against-codebase
description: >
  Use when reviewing a plan, spec, or piece of code BEFORE dispatching it to subagents
  or committing — to catch project-convention conflicts (wrong HTTP status codes, references
  to model fields that don't exist, hand-rolled UI patterns that have a documented primitive,
  shadcn API shapes that differ from base-ui, error shapes that don't match the project's
  error factory). Triggers on phrases like "verify this plan", "check this for project
  conventions", "audit before dispatch", "is this consistent with the codebase". Run BEFORE
  the work happens — corrections are cheapest before commitment.
---

# verify-against-codebase

## Why this skill exists

A plan or spec written from generic defaults will drift from a project's actual conventions. Examples this project has hit:

- ASP.NET `Program.cs` configures `InvalidModelStateResponseFactory` to return **422 Unprocessable Entity**, not 400 — but plans written from defaults assume 400.
- The `Account` model is intentionally minimal (no `OpeningBalance`, `OpeningBalanceDate`, `CreatedAt` fields per the project's "no derived columns" rule) — but plans assume those fields exist because they're common in finance apps.
- The shadcn `<Badge>` has been extended with `success`/`warning`/`info` variants — but new code keeps hand-rolling `bg-{semantic}/10 text-{semantic}` spans.
- shadcn primitives use `@base-ui/react`, not Radix — `<PopoverTrigger render={...}>` not `asChild`, `<TabsRoot onValueChange={(v, eventDetails) => ...}>` not Radix's signature, `<MenuItem onClick>` not `onSelect`.

Each of these caused implementer rework during the Movements + Quick-Add slice. Catching them before dispatch saves a round trip.

## When to use

Run before:
- Committing to a written plan (after `writing-plans`, before `subagent-driven-development`).
- Dispatching a long-running subagent.
- Committing a non-trivial code change.

Run also when:
- The user says "verify this", "audit this against the codebase", "check this for project conventions".
- A subagent reports `DONE_WITH_CONCERNS` flagging a possible convention mismatch.

Do NOT run for:
- Trivial typo fixes.
- Pure documentation updates (README polish, comment fixes).
- Code that already passed a similar check earlier in the session and hasn't changed.

## The 8-step pre-flight

For each artifact you're verifying, read the following in order. Skip any step where the artifact obviously doesn't touch that area (e.g. skip §3 if the artifact is purely frontend).

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

### 5. shadcn primitive baseline — `ProjectCeres.Client/components.json` + `src/components/ui/<primitive>.tsx`

Confirm:
- `style: base-nova`, `baseColor: zinc`, `cssVariables: true` (the established stack).
- Project uses `@base-ui/react` under the hood, NOT Radix. API differences:
  - `<PopoverTrigger render={<Button>...</Button>}>` instead of Radix `asChild`.
  - `<MenuItem onClick={...}>` instead of Radix `onSelect`.
  - `<Tooltip delay={0}>` instead of Radix `delayDuration`.
  - `<Tabs onValueChange={(value, eventDetails) => ...}>` — second param exists, type `(value: TabKey) => void` will fail TS check.
- Existing variants on the primitive being used (e.g. `<Badge>` has `default`, `secondary`, `outline`, `destructive`, `ghost`, `link`, `success`, `warning`, `info`).
- Whether the primitive has been customized (e.g. `<Button>` has project `cursor-pointer` override).

### 6. Design system primitives — `docs/design-system.md`

For every UI pattern the artifact introduces, confirm there isn't already a project primitive that does it:
- Tabular numerics → `<Numeric>`, never inline `font-mono`.
- KPI tiles → `<StatTile>` (vertical), `<StatRow>` (horizontal), `<EquationRow>` (compact muted), `<Tile>` (surface wrapper).
- Status badges → `<Badge variant="success|warning|info">`, never hand-rolled `bg-{x}/10 text-{x}` spans.
- Card error+retry → `<CardError section onRetry>` from `src/app/components/`.
- Toasts → `import { toast } from 'sonner'`, never window.alert or custom DOM banners.
- Chart colors → `chartColors` util returning `var(--chart-N)`, never hex literals or hardcoded brand colors.

### 7. Razor vs SPA boundary

Confirm:
- New SPA code does not reference Razor-only patterns (server-rendered ViewBag, MVC RedirectToAction returning a view, hidden `_RequestVerificationToken` inputs).
- New Razor changes do not reference SPA-only patterns (React hooks in a .cshtml file, `useApi` outside a React tree).
- The migration boundary in `docs/planning-phase3-spa-migration.md` is respected — don't modify a Razor controller for a feature whose SPA migration is in flight.

### 8. Active session memory & open questions

Skim the most recent commits (`git log --oneline -20`) and check `docs/planning-resolved.md` for decisions that override earlier docs. A doc may say one thing but planning-resolved settled it differently.

## Output format

Report a short list:

```
✅ verify-against-codebase: N conventions verified.
   M conflicts found:

   1. [file:line in artifact] Asserts 400; project returns 422.
      → Change to HttpStatusCode.UnprocessableEntity.
   2. [file:line in artifact] Sets Account.OpeningBalance; field doesn't exist on the model.
      → Drop. Account has only Id/Name/AccountTypeId/CurrencyId/IsActive + a few optional fields.
   3. [file:line in artifact] Hand-rolls a span with bg-success/10 text-success.
      → Use <Badge variant="success">. See docs/design-system.md "Status badges".
```

If there are no conflicts:

```
✅ verify-against-codebase: clean. Safe to dispatch.
```

## Red flags — STOP before dispatching

- Any HTTP status code in the artifact that isn't documented in `docs/api-contract.md`.
- Any reference to a model field you didn't verify by reading the model class.
- Any inline UI pattern that has a `docs/design-system.md` entry.
- Any shadcn API call (`asChild`, `onSelect`, `delayDuration`) that's a Radix idiom — base-ui differs.
- A plan that says "Returns 400 with ValidationProblemDetails" — that combo doesn't exist in this project.

If any red flag is present, fix the artifact before dispatching. Catching it pre-dispatch costs minutes; catching it mid-execution costs a subagent round trip plus rework.

## What this skill does NOT do

- Does not run tests. Run `dotnet test` and `pnpm test` separately.
- Does not lint or type-check. Run `pnpm tsc --noEmit` separately.
- Does not validate spec completeness — that's the spec-reviewer subagent's job.
- Does not validate code quality — that's the code-quality-reviewer subagent's job.

This skill is a project-convention pre-flight, nothing more.
