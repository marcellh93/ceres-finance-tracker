---
name: frontend-orchestrator
description: Use for ANY change under ProjectCeres.Client/ (React/TS, styling, layout, copy, accessibility, perf) and for ANY frontend review or refinement request. Routes between `frontend-design`, `vercel-react-best-practices`, `web-design-guidelines`, `impeccable` (23 commands), and the project's `docs/design-system.md` according to which phase the work is in — discovery, build, refine, simplify, harden, or system maintenance. Fires on phrases like "build the X page", "design this component", "make this bolder/quieter/tighter", "review this UI", "polish before merge", "fix the empty state", "extract this into the design system". The goal is to stop ad-hoc tool reaches and run frontend work through one opinionated pipeline.
---

# frontend-orchestrator

You are doing frontend work in this project. Five tools are in scope (`frontend-design`, `vercel-react-best-practices`, `web-design-guidelines`, the global `impeccable` skill with 23 commands, and the project's own `docs/design-system.md`). Each is sharp at one thing and dull at the others. This skill encodes which to reach for, in what order, for the phase of work in front of you.

**Rigid for routing** — follow the phase procedure exactly. **Flexible for content** — adapt prose, brief shape, and depth of review to the surface.

**How to use this file: classify the phase from the routing table below, then read that phase's reference file and follow it top to bottom.** Load only the phase you need. If you can't classify confidently, ask the user one clarifying question in plain English ("is this a new surface or a refinement of an existing one?") rather than guessing.

## Routing decision table

| User intent | Phase | First tool | Procedure |
|---|---|---|---|
| "build the X page" (no current impl) | 1 | `/impeccable shape` | [phase-1-new-surface.md](references/phase-1-new-surface.md) |
| "design a component for Y" (no current impl) | 1 | `/impeccable shape` | [phase-1-new-surface.md](references/phase-1-new-surface.md) |
| "make this bolder/quieter/tighter/etc." | 2 | matching Refine verb | [phase-2-3-refine-strip.md](references/phase-2-3-refine-strip.md) |
| "polish this", "tidy this up", multi-dim | 2 or 5 | `/impeccable polish` | [phase-2-3-refine-strip.md](references/phase-2-3-refine-strip.md) |
| "strip / simplify / less" | 3 | `/impeccable distill` | [phase-2-3-refine-strip.md](references/phase-2-3-refine-strip.md) |
| "the copy is unclear" | 3 | `/impeccable clarify` | [phase-2-3-refine-strip.md](references/phase-2-3-refine-strip.md) |
| "doesn't work on mobile / scales badly" | 3 | `/impeccable adapt` | [phase-2-3-refine-strip.md](references/phase-2-3-refine-strip.md) |
| "let me point at it in the browser and iterate" | 4 | `/impeccable live` (unverified) | [phase-4-live-browser.md](references/phase-4-live-browser.md) |
| "ready to ship / polish before merge" | 5 | `/impeccable harden` → `/optimize` → `/polish` | [phase-5-6-7-harden-maintain-review.md](references/phase-5-6-7-harden-maintain-review.md) |
| "this pattern is now in N places" | 6 | `/impeccable extract` | [phase-5-6-7-harden-maintain-review.md](references/phase-5-6-7-harden-maintain-review.md) |
| "review this UI" | 7 | `/impeccable critique` | [phase-5-6-7-harden-maintain-review.md](references/phase-5-6-7-harden-maintain-review.md) |
| "audit accessibility" | 7 | `web-design-guidelines` | [phase-5-6-7-harden-maintain-review.md](references/phase-5-6-7-harden-maintain-review.md) |
| "review for perf" / "is this slow" | 7 | `vercel-react-best-practices` (then `/impeccable optimize` if measurements are needed) | [phase-5-6-7-harden-maintain-review.md](references/phase-5-6-7-harden-maintain-review.md) |
| "score this against the design system" | 7 | `/impeccable polish` | [phase-5-6-7-harden-maintain-review.md](references/phase-5-6-7-harden-maintain-review.md) |

## Source-of-truth choices for this project

Impeccable expects `PRODUCT.md` (audience, brand voice, anti-references, register) and `DESIGN.md` (tokens, components, do's/don'ts) at the repo root by default. For this project both live under `docs/` — the impeccable loader's root → `.agents/context/` → `docs/` fallback chain resolves them there.

- **`docs/design-system.md` IS the DESIGN.md.** It documents every token in `ProjectCeres.Client/src/index.css`, every recipe, the do's and don'ts, and the working rules. When invoking any impeccable command, tell it explicitly: *"Read `docs/design-system.md` instead of looking for `DESIGN.md`. It is the same contract in a different filename."*
- **`docs/PRODUCT.md` exists** (created 2026-06-21 via `/impeccable teach`); the repo root is kept clean.
- **CLAUDE.md's "Frontend Work" section** stays authoritative for project gates. This skill calls into that flow — it doesn't replace it.
- **The design system is a living contract — propose extensions proactively.** When the work suggests a new primitive, token, or recipe would help (a one-off pattern that could be reused, a missing variant, a gap in the empty/error/loading vocabulary, an aesthetic improvement the user hasn't named), surface it as part of the phase output — don't wait to be asked. Treat proposals as opening moves: the user reviews and approves before extraction lands. Phase 6 is the formal ratification path; this clause sanctions naming candidates from any phase. The goal is to grow `docs/design-system.md`, not preserve it as a fixed inventory.

**Phase 0 (bootstrap) is satisfied and never re-runs.** Both files exist. If `docs/design-system.md` has drifted from a recently shipped primitive, run `sync-docs` before proceeding — never `/impeccable document`.

## Things this skill explicitly prevents

- **Reaching for `frontend-design` when the surface already exists and the user named a dimension to refine.** That's Phase 2. `frontend-design` does whole-surface bold passes, not targeted refinement.
- **Running `/impeccable craft` after `/impeccable shape`.** Use `frontend-design` for the build step in this project — sharper on aesthetic point-of-view, and integrated with the existing CLAUDE.md flow.
- **Running `/impeccable document` ever.** `docs/design-system.md` is the contract; document would create a parallel file and split the source of truth. Forbidden.
- **Running `/impeccable teach` more than once per project.** After Phase 0, `PRODUCT.md` is edited directly, never regenerated.
- **Skipping `/impeccable critique` after a Phase 1 or Phase 2 pass.** The detector and persona scoring are the highest-value thing impeccable does over `frontend-design`. Don't skip them.
- **Claiming a Phase 5 pass without all four sub-steps.** Harden → optimize → polish → web-design-guidelines.
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

If a phase calls for a tool that isn't available in this session (e.g. Phase 4 needs `dotnet run` on `https://localhost:7081`), say so explicitly and either start the prerequisite or fall back to the nearest non-interactive phase.
