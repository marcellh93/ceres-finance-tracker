---
name: frontend-orchestrator
description: Use for ANY change under ProjectCeres.Client/ (React/TS, styling, layout, copy, accessibility, perf) and for ANY frontend review or refinement request. Routes between `frontend-design`, `vercel-react-best-practices`, `web-design-guidelines`, `impeccable` (23 commands), and the project's `docs/design-system.md` according to which phase the work is in — discovery, build, refine, simplify, harden, or system maintenance. Fires on phrases like "build the X page", "design this component", "make this bolder/quieter/tighter", "review this UI", "polish before merge", "fix the empty state", "extract this into the design system". The goal is to stop ad-hoc tool reaches and run frontend work through one opinionated pipeline.
---

# frontend-orchestrator

You are doing frontend work in this project. There are five tools in scope (`frontend-design`, `vercel-react-best-practices`, `web-design-guidelines`, the local `impeccable` skill with 23 commands, and the project's own `docs/design-system.md`). Each is sharp at one thing and dull at the others. This skill encodes which to reach for, in what order, for the phase of work in front of you.

This skill is **rigid** for routing — follow the phase procedure exactly. It is **flexible** for content — adapt prose, brief shape, and depth of review to the surface.

## Why this skill exists

Without it, the default failure mode is: reach for `frontend-design`, produce one bold pass, run a manual eye-pass, ship. That skips the *detector* (impeccable's 29 deterministic anti-pattern rules), the *scored review* (`/impeccable critique`'s Nielsen + persona pass), and the *hardening pass* — which is exactly where impeccable earns its keep over `frontend-design` alone. CLAUDE.md already names the right sequencing in prose; this skill turns it into a procedure with named phases the model follows mechanically.

## Source-of-truth choices for this project

Impeccable expects two files, `PRODUCT.md` (audience, brand voice, anti-references, brand-vs-product register) and `DESIGN.md` (tokens, components, do's/don'ts in Google Stitch format), at the repo root by default. For this project both live under `docs/` (the impeccable loader's fallback chain resolves them there):

- **`docs/design-system.md` IS the DESIGN.md.** It already documents every token in `ProjectCeres.Client/src/index.css`, every recipe, the do's and don'ts, and the working rules. Do **not** create a parallel `DESIGN.md` at the repo root. Do **not** run `/impeccable document`. When invoking any impeccable command, explicitly tell it: *"Read `docs/design-system.md` instead of looking for `DESIGN.md`. It is the same contract in a different filename."*
- **`PRODUCT.md` has no local equivalent and DOES need to exist** for impeccable's discovery, critique, and register-aware Refine commands to behave correctly. It lives at **`docs/PRODUCT.md`** (created 2026-06-21 via `/impeccable teach`); the repo root is kept clean. The loader resolves it there via its root → `.agents/context/` → `docs/` fallback.
- **CLAUDE.md's "Frontend Work" section** stays authoritative for project-specific gates (show rendered result, wait for explicit approval before commit, run the UX/UI verification checklist; the manual-test handoff Stop gate / Phase H enforces the last one). This skill calls into that flow — it doesn't replace it.
- **The design system is a living contract — propose extensions proactively.** When the work suggests a new primitive, token, or recipe would help (a one-off pattern that could be reused, a missing variant, a gap in the empty/error/loading vocabulary, an aesthetic improvement the user hasn't named), surface it as part of the phase output — don't wait to be asked. Treat proposals as opening moves: the user reviews and approves before extraction lands. Phase 6 (system maintenance) is the formal ratification path; this clause sanctions naming candidates from any phase. The goal is to grow `docs/design-system.md`, not to preserve it as a fixed inventory.

## Phase 0 — Project bootstrap gate (runs once, then never again)

Before any other phase touches code or runs a critique, check both of these exist:

1. `docs/PRODUCT.md` exists (this project keeps it under `docs/`, not the repo root — already created as of 2026-06-21, so this gate is satisfied). If it were ever missing, run `/impeccable teach` and walk the user through the discovery interview. The output is `PRODUCT.md` only — **suppress DESIGN.md creation** by telling teach: *"DESIGN.md already exists at `docs/design-system.md`. Write `PRODUCT.md` to `docs/PRODUCT.md` only; do not generate a DESIGN.md."*
2. `docs/design-system.md` is up to date with anything new the user has shipped since the last edit. If a recent commit added a new primitive that isn't documented there, run the `sync-docs` skill before proceeding — not `/impeccable document`.

After Phase 0, never re-run it. If you find yourself reaching for `/impeccable teach` a second time, stop — `PRODUCT.md` already exists and should be *edited* directly, not regenerated.

## Phase 1 — New surface (a page/feature/component that does not yet exist)

The user said "build the X page" or "design a component for Y" and there is no current implementation.

1. **`/impeccable shape`** — interview the user about purpose, audience, business goal, anti-references. Output is a written brief and direction. **The user must explicitly approve the brief before any code is written.** This is impeccable's own gate (v2 hardened against autonomous agents) and you must respect it — do not infer approval from silence or from the user saying "ok let's go".
2. **`frontend-design` skill** — do the initial code pass with bold aesthetic commitment, grounded in the approved brief plus `docs/design-system.md` tokens. Use `frontend-design` here rather than `/impeccable craft` because `frontend-design` is sharper on aesthetic point-of-view and is already wired into the project's CLAUDE.md flow. Always pull tokens from `docs/design-system.md` — never hard-code values; if a token is missing, add it to `index.css` and document it there first (CLAUDE.md rule).
3. **`/impeccable critique`** — runs the deterministic detector (29 anti-pattern rules), scores against Nielsen's 10 heuristics, dispatches persona sub-agents in parallel, writes a snapshot to `.impeccable/critique/`. Treat **P0 and P1 findings as blocking**; P2/P3 are negotiable.
4. **Loop on findings** — address P0/P1 by editing the implementation, re-run `/impeccable critique` on the same target. Snapshots accumulate; the tool reads the prior one as context. If a finding is an intentional deviation, add it to `.impeccable/critique/ignore.md` with a one-line reason — do not silently re-trigger it next run.
5. **`vercel-react-best-practices`** — perf pass on the React code (re-render hygiene, bundle/import patterns). Skip the `server-*` rule family (project is SSR-via-Razor + SPA, no RSC). Read `bundle-dynamic-imports` as `React.lazy` (no `next/dynamic`).
6. **`web-design-guidelines`** — final accessibility/UX audit on the changed files. CLAUDE.md already requires this.
7. **UX/UI verification checklist** from `docs/design-system.md` § Working rules — golden path, layout context, empty state, error state, 375px mobile, all navigation links. CLAUDE.md already requires this. If browser access is unavailable, say so and hand the checklist to the user with specific URLs.
8. **Show the rendered result and wait for explicit approval** before committing. CLAUDE.md rule, not negotiable.

## Phase 2 — Refining an existing surface (the dimension is named)

The user said "make this bolder", "tone it down", "the typography feels generic", "this needs motion", "fix the spacing", "make it more delightful", "the color story is flat", or "push this further than conventional limits".

Skip discovery entirely. Go straight to the matching Refine command:

| User says | Reach for |
|---|---|
| "feels weak / too quiet / not enough presence" | `/impeccable bolder` |
| "too loud / shouting / busy" | `/impeccable quieter` |
| "typography is generic / inconsistent / off" | `/impeccable typeset` |
| "color is flat / monochrome / needs life" | `/impeccable colorize` |
| "needs motion / feels static / transitions are abrupt" | `/impeccable animate` |
| "spacing / alignment / rhythm is off" | `/impeccable layout` |
| "functional but forgettable" | `/impeccable delight` |
| "go beyond conventional / make it technically extraordinary" | `/impeccable overdrive` (beta) |

If multiple dimensions need work, use `/impeccable polish` (design-system alignment, 5-dimension scoring with P0–P3) to identify what to refine first, then run the matching Refine command per dimension. Don't refine two dimensions in one pass — the diff becomes unreviewable.

After any Refine command: re-run `/impeccable critique` on the target before claiming done.

## Phase 3 — Stripping / clarifying (the work is "too much" or "confusing")

The user said "this is overwhelming", "strip this down", "I don't understand what this is asking me to do", "the copy is muddled", "this needs to work on mobile / on a 4K monitor / on an embedded screen".

| User says | Reach for |
|---|---|
| "strip / simplify / less / cleaner" | `/impeccable distill` |
| "the copy is unclear / users don't get what this does" | `/impeccable clarify` |
| "doesn't work on mobile / breaks at narrow widths / needs to scale up" | `/impeccable adapt` |

Then critique. Then verify on the device class that triggered the request.

## Phase 4 — Live browser iteration (interactive design session, unverified)

The user wants to **point at an element on screen** and ask for variants rather than describing a change in prose.

Run `/impeccable live`. The user browses **`https://localhost:7081`** (the Kestrel/dotnet port — same origin they use every day). Despite the React client living in `ProjectCeres.Client/`, the dev setup uses `Vite.AspNetCore`'s `UseViteDevelopmentServer` middleware (see `ProjectCeres/Program.cs` ~L582) to run Vite *inside* the dotnet pipeline. Vite's HMR WebSocket is explicitly terminated at the dotnet port via `clientPort: 7081` in `ProjectCeres.Client/vite.config.ts`. So HMR works, but it reaches the browser through Kestrel's reverse proxy rather than from Vite's standalone port 5173.

**This phase is unverified.** Impeccable docs describe `/impeccable live` as working on "Vite, Next.js (including monorepos), SvelteKit, Astro, Nuxt" but say nothing about Vite hosted behind a dotnet middleware. There are two ways it could discover the dev server — by inspecting the Vite process (works fine for us) or by probing port 5173 directly (would miss us, since clients hit 7081). The first time this phase runs, **treat it as an experiment**: try it, watch for "no Vite process found" or "HMR not connected" errors, and report back. If it works, leave Phase 4 in place. If it fails, fall back to Phase 1 or 2 and add a one-line note here documenting the failure mode so it isn't retried indefinitely.

**Prerequisites before invoking:**
- `dotnet run --project ProjectCeres` running on `https://localhost:7081`.
- The browser session is on the React-served path (`/app/...`), not a pure Razor path. The Razor layer (Tailwind v3, server-rendered) has no HMR and is out of scope for live mode regardless.

**Caveats:**
- After accepting a variant, **the working-rule consistency clause applies**: a change to a shared primitive must be propagated everywhere it's used in the same pass (CLAUDE.md rule + `docs/design-system.md` § Working rules #5). Do this manually after the live session ends.
- The Razor layer (`ProjectCeres/Views/`, Tailwind v3) cannot be live-iterated. For Razor view changes, fall back to Phase 1 or 2.

## Phase 5 — Hardening before merge

The user said "ready to ship", "polish before merge", "production-ready", "what's missing", or you're about to commit a Phase 1/2/3 result.

1. **`/impeccable harden`** — edge cases, i18n, empty states, error states, overflow, long-text, missing-data. (`/onboard` merged into `/harden` in impeccable v2.1; first-run experiences and empty states are now part of harden's scope.)
2. **`/impeccable optimize`** — LCP, bundle size, runtime perf. Distinct from `vercel-react-best-practices` — that skill enforces React patterns; this one diagnoses actual measurements.
3. **`/impeccable polish`** — final 5-dimension design-system alignment pass with P0–P3 severity. Catches drift between what was built and what `docs/design-system.md` documents.
4. **`web-design-guidelines`** — final audit. CLAUDE.md already requires this.
5. **UX/UI verification checklist** — same as Phase 1 step 7.

## Phase 6 — System maintenance (after shipping a pattern used in 2+ places)

The user said "this pattern is now in three places, extract it", or you notice a recipe being copy-pasted across components.

1. **`/impeccable extract`** — identifies the reusable component, token, or pattern. Tell it: *"Write the extracted primitive into `ProjectCeres.Client/src/components/ui/` (shadcn convention). Add any new token to `ProjectCeres.Client/src/index.css`. Do not write a `DESIGN.md` — the project's design system lives at `docs/design-system.md`."*
2. **`sync-docs` skill** — update `docs/design-system.md` to document the new primitive, its variants, and when to use it. Same commit as the extraction.

## Phase 7 — Pure review (no implementation request)

The user said "review this UI", "audit this page", "what's wrong with this", "check this against the design system".

- **For a single dimension** (typography, color, motion, layout): `/impeccable polish` — 5-dimension scoring, design-system alignment.
- **For overall usability and quality**: `/impeccable critique` — Nielsen heuristics, persona sub-agents, detector.
- **For accessibility specifically**: `web-design-guidelines` first, then `/impeccable critique` for the rest.
- **For perf specifically**: `vercel-react-best-practices` first (pattern-level prevention — re-render hygiene, bundle/import). Escalate to `/impeccable optimize` only if measurements are needed (LCP, INP, CLS). The two skills are complements, not substitutes: prevention catches patterns at write-time; optimize diagnoses the production app's actual bottlenecks.
- **For high-stakes surfaces** (sign-up flow, billing, account deletion): run all three — `polish`, `critique`, and `web-design-guidelines`. Cost is worth it.

## Routing decision table

When the user's request is ambiguous, classify with this table before reaching for a tool:

| User intent | Phase | First tool |
|---|---|---|
| "build the X page" (no current impl) | 1 | `/impeccable shape` |
| "design a component for Y" (no current impl) | 1 | `/impeccable shape` |
| "make this bolder/quieter/tighter/etc." | 2 | matching Refine verb |
| "polish this", "tidy this up", multi-dim | 2 or 5 | `/impeccable polish` |
| "strip / simplify / less" | 3 | `/impeccable distill` |
| "the copy is unclear" | 3 | `/impeccable clarify` |
| "doesn't work on mobile / scales badly" | 3 | `/impeccable adapt` |
| "let me point at it in the browser and iterate" | 4 | `/impeccable live` (browser at `https://localhost:7081`; unverified, see phase notes) |
| "ready to ship / polish before merge" | 5 | `/impeccable harden` → `/optimize` → `/polish` |
| "this pattern is now in N places" | 6 | `/impeccable extract` |
| "review this UI" | 7 | `/impeccable critique` |
| "audit accessibility" | 7 | `web-design-guidelines` |
| "review for perf" / "is this slow" | 7 | `vercel-react-best-practices` (then `/impeccable optimize` if measurements are needed) |
| "score this against the design system" | 7 | `/impeccable polish` |

## Things this skill explicitly prevents

- **Reaching for `frontend-design` when the surface already exists and the user named a dimension to refine.** That's Phase 2. `frontend-design` does whole-surface bold passes, not targeted refinement.
- **Running `/impeccable craft` after `/impeccable shape`.** Use `frontend-design` for the build step in this project — it's sharper on aesthetic point-of-view and integrates with the existing CLAUDE.md flow.
- **Running `/impeccable document` ever.** `docs/design-system.md` is the contract; running document would create a parallel file and split the source of truth. Forbidden.
- **Running `/impeccable teach` more than once per project.** After Phase 0, `PRODUCT.md` is edited directly, never regenerated.
- **Skipping `/impeccable critique` after a Phase 1 or Phase 2 pass.** The detector and the persona scoring are the highest-value thing impeccable does over `frontend-design`. Don't skip them.
- **Claiming a Phase 5 pass without all four sub-steps.** Harden → optimize → polish → web-design-guidelines. Skipping any step on the path to merge is a quality regression.
- **Treating a Refine pass as final.** Always re-critique after refinement before claiming done.

## What stays unchanged from CLAUDE.md

- Read `docs/design-system.md` first. Use existing tokens; never hard-code values.
- Missing tokens go in `index.css` first, documented in `docs/design-system.md`, then consumed.
- Show the rendered result and wait for explicit approval before commit.
- Cross-codebase consistency: a change to a shared primitive applies everywhere in the same pass.
- The UX/UI verification checklist is mandatory after implementation. Hand it to the user with specific URLs if you can't run a browser yourself.
- `pnpm --dir ProjectCeres.Client …`, never `cd`-then-`pnpm`. Never `npm`.
- The React client is **web-only**. Skip React Native rules in `vercel-react-best-practices`.
- The project uses CSS-based view transitions, not React's `<ViewTransition>` (canary-only). Treat `vercel-react-view-transitions` as a future-migration reference.

## How to invoke this skill yourself

When you start any frontend task, before reaching for any tool, identify the phase from the routing table, then follow that phase's procedure top to bottom. If you can't classify the phase confidently, ask the user one clarifying question (which phase, in plain English: "is this a new surface or a refinement of an existing one?") rather than guessing.

If a phase calls for a tool that isn't available in this session (e.g., Phase 4 needs `dotnet run` going on `https://localhost:7081`), say so explicitly and either start the prerequisite or fall back to the nearest non-interactive phase.
