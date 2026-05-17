---
name: verify-frontend
description: >
  Use when reviewing a plan, spec, or piece of code that touches the React client
  (anything under ProjectCeres.Client/, *.tsx, *.ts, shadcn/Tailwind/design-system code)
  BEFORE dispatching it to subagents or committing — to catch frontend-side
  project-convention conflicts (hand-rolled UI patterns that have a documented primitive,
  shadcn API shapes that differ from base-ui, design-system token violations, missing
  design-system primitives). Triggers on phrases like "verify this plan",
  "check this for project conventions", "audit before dispatch",
  "is this consistent with the codebase" — when the artifact is frontend-scoped.
  Run BEFORE the work happens — corrections are cheapest before commitment.
---

# verify-frontend

This skill is the frontend half of the verify-against-codebase split. For backend artifacts use `verify-backend`. For mixed artifacts invoke both. The legacy `verify-against-codebase` skill is a router that dispatches to one or both based on the diff.

## Why this skill exists

A plan or spec written from generic defaults will drift from this project's actual client conventions. Examples this project has hit:

- The shadcn `<Badge>` has been extended with `success`/`warning`/`info` variants — but new code keeps hand-rolling `bg-{semantic}/10 text-{semantic}` spans.
- shadcn primitives use `@base-ui/react`, not Radix — `<PopoverTrigger render={...}>` not `asChild`, `<TabsRoot onValueChange={(v, eventDetails) => ...}>` not Radix's signature, `<MenuItem onClick>` not `onSelect`. Each of these caused implementer rework during the Movements + Quick-Add slice.
- Tabular numerics keep getting rendered as inline `font-mono` spans instead of `<Numeric>` — losing the project's locale-aware formatting and right-alignment.

Catching these before dispatch saves a round trip.

## When to use

Run before:
- Committing to a written plan that touches React / TypeScript / Tailwind / shadcn code (after `writing-plans`, before `subagent-driven-development`).
- Dispatching a long-running subagent at frontend work.
- Committing a non-trivial client-side change.

Run also when:
- The user says "verify this", "audit this against the codebase", "check this for project conventions" — and the artifact is frontend-scoped.
- A subagent reports `DONE_WITH_CONCERNS` flagging a possible client-side convention mismatch.

Do NOT run for:
- Trivial typo fixes.
- Pure documentation updates.
- Pure backend artifacts — use `verify-backend` instead.
- Code that already passed a similar check earlier in the session and hasn't changed.

## The 5-step pre-flight (frontend)

For each artifact you're verifying, read the following in order.

### 1. Project rules — `CLAUDE.md`

Skim every bullet, with extra attention to the **Frontend Work** section. The design-system-first rule and the post-implementation UX/UI verification checklist are load-bearing. If the artifact violates any of them, that's a stop-the-line finding.

### 2. shadcn primitive baseline — `ProjectCeres.Client/components.json` + `src/components/ui/<primitive>.tsx`

Confirm:
- `style: base-nova`, `baseColor: zinc`, `cssVariables: true` (the established stack).
- Project uses `@base-ui/react` under the hood, NOT Radix. API differences:
  - `<PopoverTrigger render={<Button>...</Button>}>` instead of Radix `asChild`.
  - `<MenuItem onClick={...}>` instead of Radix `onSelect`.
  - `<Tooltip delay={0}>` instead of Radix `delayDuration`.
  - `<Tabs onValueChange={(value, eventDetails) => ...}>` — second param exists, type `(value: TabKey) => void` will fail TS check.
- Existing variants on the primitive being used (e.g. `<Badge>` has `default`, `secondary`, `outline`, `destructive`, `ghost`, `link`, `success`, `warning`, `info`).
- Whether the primitive has been customized (e.g. `<Button>` has project `cursor-pointer` override).

### 3. Design system primitives — `docs/design-system.md`

For every UI pattern the artifact introduces, confirm there isn't already a project primitive that does it:
- Tabular numerics → `<Numeric>`, never inline `font-mono`.
- KPI tiles → `<StatTile>` (vertical), `<StatRow>` (horizontal), `<EquationRow>` (compact muted), `<Tile>` (surface wrapper).
- Status badges → `<Badge variant="success|warning|info">`, never hand-rolled `bg-{x}/10 text-{x}` spans.
- Card error+retry → `<CardError section onRetry>` from `src/app/components/`.
- Toasts → `import { toast } from 'sonner'`, never window.alert or custom DOM banners.
- Chart colors → `chartColors` util returning `var(--chart-N)`, never hex literals or hardcoded brand colors.

### 4. Razor vs SPA boundary (frontend side)

Confirm:
- New SPA code does not reference Razor-only patterns (server-rendered ViewBag, MVC RedirectToAction returning a view, hidden `_RequestVerificationToken` inputs).
- The migration boundary in `docs/planning-phase3-spa-migration.md` is respected — don't reach back into a Razor view for a feature whose SPA migration is in flight.

### 5. Active session memory & open questions

Skim the most recent commits (`git log --oneline -20`) and check `docs/planning-resolved.md` for decisions that override earlier docs. A doc may say one thing but planning-resolved settled it differently. The design-system doc itself evolves — confirm tokens / primitives named in the artifact match the current state.

## Output format

Report a short list:

```
verify-frontend: N conventions verified.
   M conflicts found:

   1. [file:line in artifact] Hand-rolls a span with bg-success/10 text-success.
      → Use <Badge variant="success">. See docs/design-system.md "Status badges".
   2. [file:line in artifact] Uses Radix `asChild` on PopoverTrigger.
      → base-ui uses render={...} prop. See ProjectCeres.Client/src/components/ui/popover.tsx.
```

If there are no conflicts:

```
verify-frontend: clean. Safe to dispatch.
```

## Red flags — STOP before dispatching

- Any inline UI pattern that has a `docs/design-system.md` entry.
- Any shadcn API call (`asChild`, `onSelect`, `delayDuration`) that's a Radix idiom — base-ui differs.
- Any `font-mono` span used for numeric display — should be `<Numeric>`.
- Any hex color literal or hard-coded brand color in a chart — should be `chartColors`.
- Any window.alert / custom DOM toast — should be sonner's `toast()`.

If any red flag is present, fix the artifact before dispatching.

## What this skill does NOT do

- Does not run tests. Run `pnpm test` separately, scoped per `feedback_never_skip_tests_to_make_them_pass.md`'s tier rule (TIER F for `.ts/.tsx`-only diffs).
- Does not type-check. Run `pnpm tsc --noEmit` separately.
- Does not audit backend artifacts — use `verify-backend`.
- Does not run accessibility / visual-design audits — those are `web-design-guidelines` and `impeccable`.

This skill is a frontend-convention pre-flight, nothing more.
