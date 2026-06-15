# ceres-researcher agent + Phase A wiring — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (or executing-plans). Steps use checkbox (`- [ ]`).

**Goal:** Add a 6th `ceres-*` agent (`ceres-researcher`, a pre-design fact-finder), generalize the dispatcher-gate rule so the agent isn't rejected, and wire it into Phase A as brainstorming's optional first step.

**Architecture:** One new agent file (deny-list frontmatter, four-section brief contract), two doc edits to the dispatcher-gate rule (CLAUDE.md + docs/agents.md — must ship together with the agent or it loops), one constitution Phase A note, one hook-text line. No app code.

**Spec:** `docs/superpowers/specs/2026-06-15-ceres-researcher-agent-phase-a-design.md`. **Branch:** `tooling-ceres-researcher-agent` (spec committed `31452c8`).

## Key facts (verified this session)
- `ceres-*` frontmatter convention: `disallowedTools` deny-list (NEVER `tools:` allow-list — typo-footgun grants all tools), `model: inherit`. `ceres-tech-lead` already keeps read-only Bash (`disallowedTools: Write, Edit, NotebookEdit`) — the researcher matches that exactly.
- The dispatcher-gate rule lives in **two** places that must stay in sync: `CLAUDE.md:136-144` and `docs/agents.md:39-41`. Both hard-code `## Conflicts found` as the required second preamble section. This is the load-bearing edit — the agent is non-functional (infinite re-dispatch) without it.
- `stage-start-detect.js` builds `additionalContext` as a `.join("\n")` array; the last element is the "If the request is genuinely small… skip the brainstorm. Otherwise: invoke `superpowers:brainstorming` next." line.
- Constitution Phase A is at `references/constitution.md:16-35`, advisory, with Phase A′/A″ as sibling advisory sub-notes after it.
- docs/agents.md: `## The five roles` heading (line 9) + a 5-row table; `## The contract` (21); `## The dispatcher gate` (39).
- New agent files need a session restart to load; `/agents` UI is the only way to audit effective tools (no CLI/log).

## File map
- Create: `.claude/agents/ceres-researcher.md`
- Modify: `CLAUDE.md` (dispatcher-gate rule generalization)
- Modify: `docs/agents.md` (gate generalization + 6th role row + contract note + "five"→"six")
- Modify: `.claude/skills/playbook/references/constitution.md` (Phase A note)
- Modify: `.claude/skills/playbook/hooks/stage-start-detect.js` (one additionalContext line)

---

### Task 1: Generalize the dispatcher-gate rule (do FIRST — the agent is dead without it)

**Files:** Modify `CLAUDE.md`, `docs/agents.md`

- [ ] **Step 1: Edit CLAUDE.md.** Replace the gate rule's item 2 (line ~139):
  - Old: `2. Both preamble sections are present and ordered: `## What I read`, then `## Conflicts found`, then the answer.`
  - New: `2. Both preamble sections are present and ordered: `## What I read` first, then the role's **designated second section** — `## Conflicts found` for the review-lens roles (architect / cto / pm / security-reviewer / tech-lead), or `## Subsystems & files touched` for `ceres-researcher` (the pre-design fact-finder, which has no design to find conflicts with) — then the rest of the body.`

- [ ] **Step 2: Edit docs/agents.md gate section** (line ~41). In the sentence `(2) both preamble sections are present and ordered (`## What I read` → `## Conflicts found` → answer)`, change to: `(2) both preamble sections are present and ordered — `## What I read` first, then the role's designated second section (`## Conflicts found` for the review-lens roles, or `## Subsystems & files touched` for `ceres-researcher`) → the rest of the body`.

- [ ] **Step 3: Verify both edits present.**
Run: `grep -n "Subsystems & files touched" CLAUDE.md docs/agents.md`
Expected: one hit in each file.

- [ ] **Step 4: Commit.**
```bash
git add CLAUDE.md docs/agents.md
git commit -m "docs(tooling): generalize dispatcher-gate 2nd-section rule for ceres-researcher

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
(Committed first so the agent in Task 2 lands into a codebase whose gate already accepts it.)

### Task 2: Create the ceres-researcher agent

**Files:** Create `.claude/agents/ceres-researcher.md`

- [ ] **Step 1: Write the file** verbatim:
```markdown
---
name: ceres-researcher
description: Pre-design fact-finder for Project Ceres. Reads the codebase BEFORE answering. Dispatch at stage-start, as the first step of brainstorming, to map the subsystems / prior art / conventions / unknowns an activity touches — BEFORE a design exists. Breadth-first discovery, not a design or a single-lens review.
disallowedTools: Write, Edit, NotebookEdit
model: inherit
---

You are the **pre-design fact-finder** for Project Ceres. You run at stage-start, before any design exists, and report the lay of the land so the brainstorm reasons from evidence instead of generic priors. You report facts; you do NOT recommend an approach or choose a design — that is the brainstorm's job, and a single-perspective call belongs to `ceres-architect`/`ceres-tech-lead`. Your deliverable is the raw material a design is built on.

You keep read-only `Bash` (like `ceres-tech-lead`) for discovery — `grep`, `find`, `git log`, `ls`. You MUST NOT mutate anything: no Write, no Edit, no Bash command that changes state (no migrations, no `dotnet run`, no file writes, no git commits). Discovery only.

## BEFORE YOU ANSWER — read first (non-negotiable)

Read these baseline files every time, regardless of the activity:

- `CLAUDE.md` — project rules, architecture rules, deletion rules, what-not-to-do
- the active phase's roadmap section (`docs/roadmap-phase-three.md`)
- the relevant `docs/planning-phase*.md` for the activity
- `docs/architecture.md` — layer model, request flow (overview read)
- `docs/security-model.md` — threat model, RLS / pre-auth rules (overview read)

Then read any files the dispatcher named, and run breadth-first discovery (`grep`/`find`/`git log`) over the subsystems the activity touches.

## Your response MUST open with these sections, in this order:

The VERY FIRST line of your response must be the literal `## What I read` heading — no lead sentence, no framing, no thinking-aloud before it. Reason silently; emit only the finished brief.

## What I read
- <path> — <one line: what you looked for in it>
- ... (every baseline file + every dispatcher-named file + every file you discovered and opened)

## Subsystems & files touched
- <where the activity lives — controllers, services, models, SPA surfaces, migrations, hooks, docs>

## Prior art & reusable primitives
- <existing code/patterns that already do part of this — name the file + what it gives you, so the brainstorm reuses instead of rebuilds (the pre-design "don't rebuild what exists" catch)>
- (or: "None found. Searched: <the specific greps/globs that came up empty>")

## Conventions & constraints
- <rules that bind the work: derived-column / RLS / 422-status / IUserOwned-five-registry conventions, relevant ADRs by number, the registries a change of this shape must land in>

## Open unknowns & risks
- <the specific questions the brainstorm must resolve before a design can be chosen — design forks, missing context, things only the user can decide>

If you answer without the `## What I read` first line, or your read-list is missing a baseline file, the dispatcher will re-dispatch you with a stricter prompt. Be RIGHT about Project Ceres as it actually is — breadth-first and evidence-backed — not fast or plausible-from-priors. This role exists because the CER003 miss (2026-05-26) happened when a perspective pass reasoned without reading.
```

- [ ] **Step 2: Validate frontmatter parses + tool guard is right.**
Run: `head -6 .claude/agents/ceres-researcher.md` and confirm `disallowedTools: Write, Edit, NotebookEdit` (NO Bash, matching tech-lead) + `model: inherit`.
Note: effective-tool audit requires the `/agents` UI after a session restart (no CLI). Flag to the user that they should eyeball `/agents` once loaded to confirm Bash is present and Write/Edit/NotebookEdit are denied.

- [ ] **Step 3: Commit.**
```bash
git add .claude/agents/ceres-researcher.md
git commit -m "feat(tooling): ceres-researcher agent — pre-design fact-finder (read-first, deny-list, keeps Bash)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

### Task 3: docs/agents.md — 6th role row + contract note

**Files:** Modify `docs/agents.md`

- [ ] **Step 1: Heading + intro counts.** Change `## The five roles` (line 9) → `## The six roles`. In the "Why these exist" paragraph (line 7), change "These five roles codify" → "These six roles codify".

- [ ] **Step 2: Add the 6th table row** after the `ceres-security-reviewer` row (line 17):
```
| `ceres-researcher` | Pre-design fact-finding: subsystems / prior art / conventions / unknowns, BEFORE a design exists | `CLAUDE.md`, the active roadmap stage section, the relevant `planning-phase*.md`, `docs/architecture.md` + `docs/security-model.md` overviews, + dispatcher-named files | `Write, Edit, NotebookEdit` (keeps read-only `Bash`, like tech-lead) | Stage-start of a non-trivial stage, as brainstorming's first step — before a design exists |
```

- [ ] **Step 3: Note the Bash carve-out** in the deny-list paragraph (line ~19, where it already explains tech-lead omits Bash): extend it to "`ceres-tech-lead` and `ceres-researcher` omit `Bash` from their deny-list because their roles run read-only `Bash` (`grep`, `find`, `git log`, `dotnet build`); their body contracts forbid mutating `Bash`."

- [ ] **Step 4: Note the researcher's distinct body** under `## The contract` (after the existing `## Conflicts found` shape description, ~line 34): add a short paragraph: "`ceres-researcher` is the exception to the `## Conflicts found` body: at research time no design exists to conflict with, so its body is four sections — `## Subsystems & files touched`, `## Prior art & reusable primitives`, `## Conventions & constraints`, `## Open unknowns & risks` — after the shared `## What I read` first line. The dispatcher gate accepts `## Subsystems & files touched` as its designated second section (see § The dispatcher gate)."

- [ ] **Step 5: Verify.**
Run: `grep -n "six roles\|ceres-researcher\|Subsystems & files touched" docs/agents.md`
Expected: heading updated, role row present, contract note present.

- [ ] **Step 6: Commit.**
```bash
git add docs/agents.md
git commit -m "docs(tooling): document ceres-researcher as the 6th role + four-section brief contract

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

### Task 4: Constitution Phase A note + hook line

**Files:** Modify `.claude/skills/playbook/references/constitution.md`, `.claude/skills/playbook/hooks/stage-start-detect.js`

- [ ] **Step 1: Constitution Phase A.** After the "Per-link hand-off" line (constitution.md:33, end of Phase A, before the `---`), add an advisory sub-note (parallel to the Phase A′/A″ style):
```markdown

**Optional pre-brainstorm research (advisory).** For a non-trivial stage, `superpowers:brainstorming`'s own step 1 ("explore project context — check files, docs, recent commits") is best done by dispatching the `ceres-researcher` agent — a read-first pre-design fact-find (subsystems / prior art / conventions / unknowns). This is brainstorming's **first internal step, not a step before it**: the brainstorm Skill is still the first thing invoked (per `feedback_brainstorm_spec_plan_execute_flow` — the first tool call is `superpowers:brainstorming`, not `Agent`); the researcher is dispatched from within that flow. Advisory, like the rest of Phase A. Skip for trivial/config-only work, same as the brainstorm skip rule.
```

- [ ] **Step 2: Hook line.** In `stage-start-detect.js`, in the `additionalContext` array, insert a new element before the final "If the request is genuinely small…" line:
```javascript
    "For a non-trivial stage, consider dispatching the `ceres-researcher` agent as brainstorming's first step — a read-first fact-find (subsystems / prior art / conventions / unknowns) so the design starts from evidence. Skip for small/config-only stages, same as the brainstorm skip rule.",
    "",
```

- [ ] **Step 3: Verify the hook still emits valid JSON.**
Run: `echo '{"prompt":"let'\''s build feature X"}' | node .claude/skills/playbook/hooks/stage-start-detect.js`
Expected: valid JSON with `hookSpecificOutput.additionalContext` containing the new `ceres-researcher` line. (Pipe through `python3 -m json.tool` to confirm it parses.)

- [ ] **Step 4: Commit.**
```bash
git add .claude/skills/playbook/references/constitution.md .claude/skills/playbook/hooks/stage-start-detect.js
git commit -m "feat(tooling): wire ceres-researcher into Phase A (constitution note + stage-start hook line)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

### Task 5: Smoke test + docs sync + changelog

- [ ] **Step 1: Smoke-dispatch the agent.** Dispatch `ceres-researcher` (via the Agent tool, `subagent_type: ceres-researcher` — note it needs a session restart to load; if unavailable this session, do it next session and note so) against a sample non-trivial activity, e.g. *"Research what building a `/settings/sessions` SPA page would touch."* Confirm the response: (a) first line is literal `## What I read`; (b) carries all four brief sections; (c) the read-list includes the baseline files. Confirm that as the orchestrator you would NOT re-dispatch under the generalized gate (the `## Subsystems & files touched` second section is now accepted).
  - If the agent isn't loaded yet (no restart), mark this step blocked-on-restart and hand the user the one-line verification to run.

- [ ] **Step 2: changelog-sync.** Add to `[Unreleased]`: under a Subagents/Tooling group — "Added: `ceres-researcher` pre-design fact-finder agent + Phase A wiring (read-first brief of subsystems/prior-art/conventions/unknowns before a design exists; advisory, dispatched as brainstorming's first step)."

- [ ] **Step 3: sync-docs.** Confirm docs/agents.md is the durable home (done in Task 3); no other doc needs it (the constitution is the playbook's own ref, edited in Task 4; no ADR needed — this applies the existing subagent pattern rather than overturning a decision). If a spec/plan cross-ref is missing, add it.

- [ ] **Step 4: Commit the changelog.**
```bash
git add CHANGELOG.md
git commit -m "docs(tooling): changelog — ceres-researcher agent + Phase A wiring

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Self-review (against the spec)
- **Spec §2 (agent file):** Task 2 — deny-list keeping Bash (tech-lead precedent), four-section contract, read-first baseline, `## What I read` first line. ✓
- **Spec §4 (gate generalization, the load-bearing fix):** Task 1 — BOTH CLAUDE.md + docs/agents.md, committed FIRST so the agent lands into an accepting gate. ✓
- **Spec §3 (distinctness):** captured in the agent body prose + the docs/agents.md role row (fact-finder, no recommendation). ✓
- **Spec §5 (Phase A wiring):** Task 4 — constitution note (framed as brainstorming's first step, resolving the brainstorming-first tension) + hook line, advisory. ✓
- **Spec §5c (docs/agents.md):** Task 3 — 6th row, "six roles", Bash note, contract note. ✓
- **Spec §6 (YAGNI):** no hard gate, no auto-dispatch, no state slot — none added. ✓
- **Spec §7 (verification):** Task 2 step 2 (frontmatter + /agents audit caveat), Task 4 step 3 (hook JSON), Task 5 step 1 (smoke dispatch). ✓
- **Spec §8 (ship gates):** agent + both gate edits ship across Tasks 1-2 (gate first, then agent — same effect as "together," and safer ordering); changelog in Task 5. The spec said "one commit"; the plan splits gate-first / agent-second so the agent never exists against an un-generalized gate — note this deviation is *stronger* than the spec's "same commit" (it removes even a transient window).
- **No placeholders:** every step has the literal text or exact command. The one runtime caveat (agent needs session restart to dispatch) is flagged with a fallback, not left ambiguous.
- **Type/name consistency:** `ceres-researcher`, `## Subsystems & files touched`, `disallowedTools: Write, Edit, NotebookEdit` are identical across the agent file, both gate edits, the docs row, and the smoke test.
