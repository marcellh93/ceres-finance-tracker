# ceres-researcher agent + Phase A wiring (design)

**Date:** 2026-06-15
**Status:** Draft — pending user review
**Origin:** User question — "do we have an agent that researches the activity before the first line of code, and is it wired into the playbook?" Answer was no (research is skill-gated, not agent-owned). This spec closes that gap.

**Locked decisions (user, 2026-06-15):** advisory-recommend trigger (no hard gate, no auto-dispatch); pre-design fact-finding brief as the role (not a recommended approach); four-section brief body; Bash carve-out kept (read-only).

**verify-against-codebase corrections folded in** (2026-06-15) — cited inline.

---

## 1. What it is

A sixth `ceres-*` strategy agent, `ceres-researcher`: the **pre-design fact-finder**. At stage-start, as the *first step of the brainstorm*, the orchestrator dispatches it to investigate the activity against the actual codebase and return a grounded brief — so the brainstorm reasons from evidence (which subsystems, what prior art, which conventions bind it, what's unknown) instead of generic priors. It is the pre-code mirror of the post-diff `reviewer-*` pipeline.

**Non-goals:** it does not propose a design/approach (that's the brainstorm + `ceres-architect`); it does not audit a finished design (that's `verify-against-codebase`); it is not a hard gate and is not auto-dispatched.

## 2. The agent file — `.claude/agents/ceres-researcher.md`

**Frontmatter** (conforms to the established `ceres-*` convention — verified against the 5 existing files):
```yaml
---
name: ceres-researcher
description: Pre-design fact-finder for Project Ceres. Reads the codebase BEFORE answering. Dispatch at stage-start, as the first step of brainstorming, to map the subsystems / prior art / conventions / unknowns an activity touches — BEFORE a design exists. Breadth-first discovery, not a design or a single-lens review.
disallowedTools: Write, Edit, NotebookEdit
model: inherit
---
```
- **`disallowedTools` deny-list, NOT `tools:` allow-list** (memory `project_ceres_agents_use_disallowedtools`: the allow-list typo-footgun silently grants all tools; the deny-list states the real invariant — never mutate code).
- **Bash kept (deny-list omits it) — exact precedent: `ceres-tech-lead`**, which already keeps read-only Bash for `grep`/`dotnet build` (docs/agents.md:14). The researcher needs `grep`/`find`/`git log` for prior-art discovery. The body contract forbids mutating Bash, same as tech-lead. This is NOT a novel exception — it matches an existing role's guard exactly.

**Read-first baseline** (every dispatch, regardless of question — it has no spec, that's the point):
- `CLAUDE.md` — project rules, architecture rules, what-not-to-do
- the active phase's roadmap section (`docs/roadmap-phase-three.md`)
- the relevant `docs/planning-phase*.md`
- `docs/architecture.md` + `docs/security-model.md` (overview read — layer model + threat model)
- plus any files the dispatcher names in the prompt
- then breadth-first discovery via Grep/Glob/Bash over the subsystems the activity names

**Response contract** — first line is the literal `## What I read` (the dispatcher-gate rule, see §4). Then the brief body, **four sections** (NOT `## Conflicts found` — there is no design to conflict with at research time):

```
## What I read
- <path> — <what I looked for>
- ... (baseline + dispatcher-named + discovered files)

## Subsystems & files touched
- <where the activity lives — controllers, services, models, SPA surfaces, migrations>

## Prior art & reusable primitives
- <existing code that already does part of this — the "don't rebuild MfaTicketService" catch, pre-design>

## Conventions & constraints
- <rules that bind the work: derived-column / RLS / 422-status / IUserOwned-five-registry conventions, relevant ADRs, the registries a change must land in>

## Open unknowns & risks
- <the specific questions the brainstorm must resolve before a design can be chosen>
```

The agent's body prose states: *"You report the lay of the land. You do NOT recommend an approach or choose a design — that is the brainstorm's job. Your deliverable is facts the brainstorm builds on."*

## 3. Why it's distinct (the redundancy resolution)

- **vs `verify-against-codebase`:** that audits a *finished design* against conventions — it needs a design as input. The researcher runs *before a design exists* and produces the raw material. Sequence: **researcher → brainstorm → design → verify-against-codebase → spec**. No overlap; they sit on opposite sides of the design.
- **vs `ceres-architect` / `ceres-tech-lead`:** those are single-lens consults you opt into for one perspective on a *specific question* (architecture trade-off; feasibility estimate). The researcher is breadth-first discovery with no opinion — it's "here's everything relevant," not "here's my take." You'd still dispatch `ceres-architect` *during* the brainstorm for a hard cross-subsystem call; the researcher just makes sure that call starts from facts.

## 4. Dispatcher-gate rule generalization (REQUIRED — verify-against-codebase finding #1)

The current gate (CLAUDE.md:138-140 + docs/agents.md:41) hard-codes the second preamble section as `## Conflicts found`:
> "Both preamble sections are present and ordered: `## What I read`, then `## Conflicts found`, then the answer."

The researcher's second section is `## Subsystems & files touched`, not `## Conflicts found` — so **as written, the gate would reject every researcher response and re-dispatch in a loop.** This spec generalizes the gate in BOTH files (same edit, same commit):

> "Both preamble sections are present and ordered: `## What I read` first, then the role's **designated second section** — `## Conflicts found` for the review-lens roles (architect/cto/pm/security-reviewer/tech-lead), or `## Subsystems & files touched` for `ceres-researcher` — then the rest of the body. The `## What I read` list includes the role's baseline files plus the dispatcher-named files."

The first-line rule (`## What I read` literal first line, no preamble) is unchanged and applies to the researcher identically.

## 5. Playbook wiring (advisory — lowest blast radius)

### 5a. `stage-start-detect.js` (existing Phase A UserPromptSubmit hook)
Extend the existing `additionalContext` string (no change to the matching regex list, no new hook). Add a line recommending the researcher as the brainstorm's first step:
> "Before/at the start of `superpowers:brainstorming`, consider dispatching `ceres-researcher` to ground the design in a codebase fact-find (subsystems / prior art / conventions / unknowns). Skip for genuinely small or config-only stages — same skip rule as the brainstorm itself."

Verify the hook still emits valid JSON after the edit (node parse).

### 5b. Constitution Phase A (`references/constitution.md`)
Document `ceres-researcher` in the Phase A section as the **optional first step *of* brainstorming** — NOT a step before it. This is the resolution of verify-against-codebase finding #3: the pinned rule `feedback_brainstorm_spec_plan_execute_flow` says the FIRST tool call on "let's build X" is `superpowers:brainstorming`, *not Agent*. The constitution note must say:
> "`superpowers:brainstorming`'s own checklist step 1 is 'explore project context — check files, docs, recent commits.' `ceres-researcher` is the codified, read-first way to do that step for a non-trivial stage. Dispatching it is part of brainstorming, not before it — the brainstorm Skill is still the first thing invoked; the researcher is its first internal move. Advisory, like the rest of Phase A. Skip for trivial/config-only work."

Document it parallel to the Phase A′/A″ style (advisory sub-note under Phase A), not as a new numbered phase.

### 5c. `docs/agents.md`
- Add `ceres-researcher` as the **6th row** in the roles table (stance: pre-design fact-finding; baseline: the §2 list; tool guard: `Write, Edit, NotebookEdit` — keeps Bash; dispatch when: stage-start of a non-trivial stage, before a design exists).
- Update "The five roles" heading → "The six roles."
- Add a subsection under "The contract" noting the researcher's body uses the four-section brief shape (not `## Conflicts found`), and that the dispatcher gate accepts `## Subsystems & files touched` as its designated second section (cross-ref §4).
- Note the Bash carve-out rationale (same as tech-lead).

## 6. What stays out (YAGNI)
- No hard gate (Phase B unchanged — still brainstorming + verify-against-codebase).
- No auto-dispatch (hooks are text-only; can't launch agents — same reason the reviewer pipeline is orchestrator-dispatched).
- No new state-file slot / evidence-bundle requirement (advisory → nothing to enforce mechanically).
- No recommended-approach in the brief (pre-empts brainstorming).

## 7. Verification (no app code — meta/tooling only)
- Agent frontmatter valid; `/agents` UI shows `ceres-researcher` with the expected effective tool set (Write/Edit/NotebookEdit denied, Bash present) — per the memory rule, the `/agents` UI is the only way to audit effective tools; session restart needed to load a new agent file.
- `stage-start-detect.js` emits valid `additionalContext` JSON after the edit (node parse + a sample-prompt run showing the new line).
- Dispatcher-gate generalization present in BOTH CLAUDE.md and docs/agents.md (same commit) — grep both for the `## Subsystems & files touched` accepted-second-section clause.
- Smoke dispatch: dispatch `ceres-researcher` against a sample non-trivial activity (e.g. "research what a `/settings/sessions` page would touch"); confirm the response opens with `## What I read` and carries the four brief sections, and that as the orchestrator I would NOT re-dispatch it under the generalized gate.
- No `dotnet`/`pnpm` build/test impact (no app code changed) — but run `dotnet build` once if any `.cs`-adjacent file is touched (none expected).

## 8. Ship gates
- Agent file + both gate-rule edits (CLAUDE.md, docs/agents.md) + hook edit + constitution note land together (the gate generalization and the new agent MUST ship in one commit — the agent is non-functional without it).
- docs/agents.md "six roles" + the constitution Phase A note synced.
- sync-docs + changelog-sync fired (this adds a durable capability — changelog "Added: ceres-researcher pre-design fact-finder + Phase A wiring").
- This is a `.claude/`-and-docs change; no roadmap stage box (it's tooling, not a roadmap stage) — but note it in the changelog and, if there's a "tooling/subagents" tracking line, there.
