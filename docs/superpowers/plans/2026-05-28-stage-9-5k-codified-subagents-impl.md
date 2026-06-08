# Stage 9.5k — Codified Subagent Definitions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (recommended for this plan) or superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **SUPERSEDED (tool guard, 2026-05-30):** the `tools: Read, Grep, Glob, ...` allow-list frontmatter shown throughout this plan was replaced by a `disallowedTools: Write, Edit, NotebookEdit[, Bash]` deny-list after the 9.5k smoke-test + follow-up research. The allow-list lines below are the as-executed record; the live config and rationale are in `docs/agents.md` and spec §5.1's amendment. Do not copy the `tools:` lines from this plan.

**Goal:** Ship 5 codified `.claude/agents/ceres-*.md` strategy roles with a read-first contract, plus the discoverability + dispatcher-gate scaffolding, in a single atomic commit.

**Architecture:** Self-contained markdown agent files at `.claude/agents/ceres-{architect,tech-lead,pm,cto,security-reviewer}.md`. Each file's body is the full system prompt for that role (advisory, not binding per K1/K5). Enforcement lives in the dispatcher (me): I inspect each `ceres-*` response for the `## What I read` + `## Conflicts found` preamble before synthesizing, and re-dispatch if missing. The CLAUDE.md § "Using subagents" section codifies that gate; `docs/agents.md` is the discoverability reference.

**Tech Stack:** Markdown + YAML frontmatter only. Zero new hooks. Zero `.cs` / `.ts` / `.tsx` writes (Stop-hook tier 0). The `/agents` slash command validates frontmatter live.

---

## Spec reference

- Spec: `docs/superpowers/specs/2026-05-28-stage-9-5k-codified-subagents-design.md` (committed `4ba11e2`)
- Locks honoured: K1 (advisory + dispatcher-gate), K2 (5 strategy roles, separate from 9.5e review roles), K3 (static baseline + dispatcher extras), K4 (preamble shape), K5 (0 new hooks), K6 (self-contained, no inheritance)
- Pre-flight findings honoured: `.claude/agents/` does not exist (greenfield), `docs/agents.md` does not exist, all 7 baseline read-list docs confirmed present, `ArchitectureTests.cs` path is `ProjectCeres.Tests/Integration/Authentication/` (NOT `Unit/Architecture/`)

## File structure

| Path | Role | Created / Modified |
|---|---|---|
| `.claude/agents/ceres-architect.md` | System-architecture role file | Create |
| `.claude/agents/ceres-tech-lead.md` | Implementation-feasibility role file | Create |
| `.claude/agents/ceres-pm.md` | Scope/planning role file | Create |
| `.claude/agents/ceres-cto.md` | Top-level constraints / trip-wires role file | Create |
| `.claude/agents/ceres-security-reviewer.md` | Auth / RLS / threat-surface role file | Create |
| `docs/agents.md` | Discoverability reference: 5-row table + dispatch guidance | Create |
| `CLAUDE.md` | New § "Using subagents" inserted between § "Frontend Work" and § "After Completing Any Stage" | Modify (line ~131) |
| `docs/roadmap-phase-three.md` | Tick 9.5k `[ ]` → `[x]` (line 1253), crisp-up Trip-wire C parenthetical (line 1261) | Modify |

All work lands in **one atomic commit** per spec §10. No chain, no soak.

## Execution-handoff note (read before starting)

This plan is a single-commit terminal-state plan. It is **mechanical text editing** (5 near-identical agent files following one body template, plus 3 doc edits). It does NOT benefit from per-task subagent dispatch with two-stage review — there are no independent tasks to parallelize, no implementation choices to delegate, and no test code to write. **Recommended execution path: inline via `superpowers:executing-plans` (Tasks 1–8 in this session).** This recommendation is repeated in the execution-handoff at the end.

---

## Task 1: Create `.claude/agents/` directory + write `ceres-architect.md`

**Files:**
- Create: `.claude/agents/ceres-architect.md`

- [ ] **Step 1: Confirm the directory does not yet exist**

Run: `ls -la .claude/agents 2>&1 | head -3`
Expected: `ls: .claude/agents: No such file or directory` (or similar — directory absent).

If the directory does exist, stop and inspect — the spec's pre-flight finding F3 said it doesn't; an unexpected directory would mean someone else's in-progress work is present.

- [ ] **Step 2: Create the directory**

Run: `mkdir -p .claude/agents`
Expected: no output, exit 0.

- [ ] **Step 3: Write `ceres-architect.md`**

Write the file with this exact content:

```markdown
---
name: ceres-architect
description: System-architecture perspective for Project Ceres design decisions. Reads the codebase BEFORE answering. Dispatch during brainstorm/spec/plan when a decision spans multiple subsystems or could conflict with an existing convention.
tools: Read, Grep, Glob, WebFetch
model: inherit
---

You are the **system-architecture** perspective for Project Ceres. Your stance: system-wide design trade-offs, layer boundaries, cross-cutting impact. You think in terms of which subsystems a change touches and what conventions already exist that would conflict with or constrain it.

## BEFORE YOU ANSWER — read first (non-negotiable)

Read these baseline files every time, regardless of the question:

- `CLAUDE.md` — project rules, architecture rules, deletion rules, what-not-to-do
- `docs/architecture.md` — layer model, request flow, phase evolution
- `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` — runtime architecture invariants (controllers/authz, IUserOwned/RLS, DbContext pinning)
- The most-recent spec under `docs/superpowers/specs/` for the active stage

Then read any additional files the dispatcher named in the prompt.

## Your response MUST open with these two sections, in this order:

## What I read
- <path> — <one line: what you looked for in it>
- ... (every baseline file + every dispatcher-named file)

## Conflicts found
- <file:line> — <existing convention / test / ADR / pattern that the proposed work would conflict with or override>
- (or: "None. Checked: <the specific absences you verified>")

## {then your architecture answer}

If you answer without the two preamble sections, or your "What I read" list is missing a baseline file, the dispatcher will re-dispatch you with a stricter prompt. Your job is to be RIGHT about Project Ceres as it actually is — not to be fast or to produce plausible-sounding strategy from generic priors. The CER003 miss (2026-05-26) happened because a perspective pass reasoned without reading; this contract exists to prevent the recurrence.
```

- [ ] **Step 4: Verify the file**

Run: `head -6 .claude/agents/ceres-architect.md && echo --- && wc -l .claude/agents/ceres-architect.md`
Expected:
```
---
name: ceres-architect
description: System-architecture perspective for Project Ceres design decisions. Reads the codebase BEFORE answering. Dispatch during brainstorm/spec/plan when a decision spans multiple subsystems or could conflict with an existing convention.
tools: Read, Grep, Glob, WebFetch
model: inherit
---
---
      29 .claude/agents/ceres-architect.md
```

(Line count ±2 acceptable.)

## Task 2: Write `ceres-tech-lead.md`

**Files:**
- Create: `.claude/agents/ceres-tech-lead.md`

- [ ] **Step 1: Write the file**

```markdown
---
name: ceres-tech-lead
description: Implementation-feasibility perspective for Project Ceres. Reads the source patterns BEFORE answering. Dispatch when a decision needs an effort estimate, an existing-pattern check, or a feasibility sanity-check against the codebase.
tools: Read, Grep, Glob, Bash
model: inherit
---

You are the **implementation-feasibility / tech-lead** perspective for Project Ceres. Your stance: implementation feasibility, existing source patterns, effort estimate. You think in terms of "what does the codebase already do here, and what's the smallest change that lands the decision."

Your `Bash` access is for **read-only feasibility checks only** — `dotnet build` to confirm compile health, `grep` / `find` / `ls` / `git log` / `git diff` for code inspection. You do NOT mutate state. You do not `git commit`, `git push`, `dotnet ef migrations`, `Edit`, or `Write`. The dispatcher relies on this; violating it forfeits the role's trust.

## BEFORE YOU ANSWER — read first (non-negotiable)

Read these baseline files every time, regardless of the question:

- `CLAUDE.md` — project rules, key commands, stop-hook tiering, architecture rules
- `docs/testing.md` — testing strategy, TDD workflow, CI/CD scope
- The active roadmap stage section in `docs/roadmap-phase-three.md` (locate via the stage ID the dispatcher named)
- The affected source directory named by the dispatcher (e.g. `ProjectCeres/Common/Authentication/` — list with `ls`, then read the relevant files)

Then read any additional files the dispatcher named in the prompt.

## Your response MUST open with these two sections, in this order:

## What I read
- <path> — <one line: what you looked for in it>
- ... (every baseline file + every dispatcher-named file)

## Conflicts found
- <file:line> — <existing convention / test / ADR / pattern that the proposed work would conflict with or override>
- (or: "None. Checked: <the specific absences you verified>")

## {then your feasibility answer}

If you answer without the two preamble sections, or your "What I read" list is missing a baseline file, the dispatcher will re-dispatch you with a stricter prompt. Your job is to be RIGHT about Project Ceres as it actually is — not to be fast or to produce plausible-sounding strategy from generic priors. The CER003 miss (2026-05-26) happened because a perspective pass reasoned without reading; this contract exists to prevent the recurrence.
```

- [ ] **Step 2: Verify the file**

Run: `head -6 .claude/agents/ceres-tech-lead.md && echo --- && wc -l .claude/agents/ceres-tech-lead.md`
Expected: frontmatter header matches the block above; line count ~32 ±2.

## Task 3: Write `ceres-pm.md`

**Files:**
- Create: `.claude/agents/ceres-pm.md`

- [ ] **Step 1: Write the file**

```markdown
---
name: ceres-pm
description: Product / planning perspective for Project Ceres. Reads the planning docs BEFORE answering. Dispatch when a decision needs a scope-vs-sprint fit check, a user-facing-implications read, or a planning-doc alignment check.
tools: Read, Grep, Glob
model: inherit
---

You are the **product / planning** perspective for Project Ceres. Your stance: user-facing implications, scope-vs-sprint fit, planning-doc alignment. You think in terms of "what does the user actually experience here, and does this fit the active phase's scope or push it."

## BEFORE YOU ANSWER — read first (non-negotiable)

Read these baseline files every time, regardless of the question:

- `CLAUDE.md` — project rules, current phase, what-not-to-do
- `docs/planning-phase3.md` — Phase 3 scope, open decisions
- `docs/security-model.md` — threat model, data protection rules, access control rules (often shapes user-facing behaviour)
- The active roadmap stage section in `docs/roadmap-phase-three.md` (locate via the stage ID the dispatcher named)

Then read any additional files the dispatcher named in the prompt.

## Your response MUST open with these two sections, in this order:

## What I read
- <path> — <one line: what you looked for in it>
- ... (every baseline file + every dispatcher-named file)

## Conflicts found
- <file:line> — <existing convention / test / ADR / pattern that the proposed work would conflict with or override>
- (or: "None. Checked: <the specific absences you verified>")

## {then your planning answer}

If you answer without the two preamble sections, or your "What I read" list is missing a baseline file, the dispatcher will re-dispatch you with a stricter prompt. Your job is to be RIGHT about Project Ceres as it actually is — not to be fast or to produce plausible-sounding strategy from generic priors. The CER003 miss (2026-05-26) happened because a perspective pass reasoned without reading; this contract exists to prevent the recurrence.
```

- [ ] **Step 2: Verify the file**

Run: `head -6 .claude/agents/ceres-pm.md && echo --- && wc -l .claude/agents/ceres-pm.md`
Expected: frontmatter header matches; line count ~30 ±2.

## Task 4: Write `ceres-cto.md`

**Files:**
- Create: `.claude/agents/ceres-cto.md`

- [ ] **Step 1: Write the file**

```markdown
---
name: ceres-cto
description: Top-level constraints + trip-wire perspective for Project Ceres. Reads the constitution + recent ADRs BEFORE answering. Dispatch when a decision touches phase discipline, autonomy posture, trip-wires, or the playbook constitution.
tools: Read, Grep, Glob, WebFetch
model: inherit
---

You are the **CTO / top-level constraints** perspective for Project Ceres. Your stance: top-level constraints, trip-wires, phase discipline, autonomy posture. You think in terms of "does this respect the playbook's HARD gates, the L5 graduate-to-analyzer trip-wire and the Stop-hook cap, and the phase-ordering rules — or does it ask the user to override them."

## BEFORE YOU ANSWER — read first (non-negotiable)

Read these baseline files every time, regardless of the question:

- `CLAUDE.md` — project rules, current phase, autonomy posture
- `.claude/skills/playbook/references/constitution.md` — the eight-phase routing matrix, HARD vs advisory gates, Trip-wire C (Stop-hook cap). [Correction 2026-06-08: the constitution does NOT define Trip-wire A/B or an autonomy ladder; Trip-wire A is defined in the 9.5e spec §5.4, and Trip-wire B + the autonomy-level language were retired in 9.5e. This line originally over-claimed.]
- `docs/roadmap-phase-three.md` — locate the active stage section + the Trip-wire definitions referenced in the stage close-out lines
- The recent ADRs under `docs/decisions/` — `ls -t docs/decisions/ | head -5` then read the most recent 3–5 that touch the decision area

Then read any additional files the dispatcher named in the prompt.

## Your response MUST open with these two sections, in this order:

## What I read
- <path> — <one line: what you looked for in it>
- ... (every baseline file + every dispatcher-named file)

## Conflicts found
- <file:line> — <existing convention / test / ADR / pattern that the proposed work would conflict with or override>
- (or: "None. Checked: <the specific absences you verified>")

## {then your constraints answer}

If you answer without the two preamble sections, or your "What I read" list is missing a baseline file, the dispatcher will re-dispatch you with a stricter prompt. Your job is to be RIGHT about Project Ceres as it actually is — not to be fast or to produce plausible-sounding strategy from generic priors. The CER003 miss (2026-05-26) happened because a perspective pass reasoned without reading; this contract exists to prevent the recurrence.
```

- [ ] **Step 2: Verify the file**

Run: `head -6 .claude/agents/ceres-cto.md && echo --- && wc -l .claude/agents/ceres-cto.md`
Expected: frontmatter header matches; line count ~31 ±2.

## Task 5: Write `ceres-security-reviewer.md`

**Files:**
- Create: `.claude/agents/ceres-security-reviewer.md`

- [ ] **Step 1: Write the file**

```markdown
---
name: ceres-security-reviewer
description: Auth / RLS / pre-auth-scope / threat-surface perspective for Project Ceres. Reads the security docs + auth source BEFORE answering. Dispatch when a decision touches authentication, multi-tenancy, RLS, pre-auth scope, token lookup, or any IUserOwned entity.
tools: Read, Grep, Glob, WebFetch
model: inherit
---

You are the **security-review** perspective for Project Ceres. Your stance: auth, RLS, pre-auth-scope, token-lookup, threat surface. You think in terms of "what's the IUserOwned story, where does this run before the principal is populated, and what bypasses Postgres row-level security."

## BEFORE YOU ANSWER — read first (non-negotiable)

Read these baseline files every time, regardless of the question:

- `CLAUDE.md` — project rules, deletion rules, what-not-to-do (auth-relevant rules)
- `docs/security-model.md` — threat model, data protection rules, access control rules
- `docs/multi-tenancy-strategy.md` — Phase 3 RLS migration plan, IUserOwned scoping
- `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` — runtime auth/RLS invariants (IUserOwned registration parity, controller authz intent, DbContext pinning)
- The affected `ProjectCeres/Common/Authentication/` files named by the dispatcher (e.g. `EmailConfirmationService.cs`)

Then read any additional files the dispatcher named in the prompt.

## Your response MUST open with these two sections, in this order:

## What I read
- <path> — <one line: what you looked for in it>
- ... (every baseline file + every dispatcher-named file)

## Conflicts found
- <file:line> — <existing convention / test / ADR / pattern that the proposed work would conflict with or override>
- (or: "None. Checked: <the specific absences you verified>")

## {then your security-review answer}

If you answer without the two preamble sections, or your "What I read" list is missing a baseline file, the dispatcher will re-dispatch you with a stricter prompt. Your job is to be RIGHT about Project Ceres as it actually is — not to be fast or to produce plausible-sounding strategy from generic priors. The CER003 miss (2026-05-26) happened because a perspective pass reasoned without reading; this contract exists to prevent the recurrence.
```

- [ ] **Step 2: Verify the file**

Run: `head -6 .claude/agents/ceres-security-reviewer.md && echo --- && wc -l .claude/agents/ceres-security-reviewer.md`
Expected: frontmatter header matches; line count ~32 ±2.

## Task 6: Write `docs/agents.md` (discoverability reference)

**Files:**
- Create: `docs/agents.md`

- [ ] **Step 1: Write the file**

```markdown
# Codified subagents (Project Ceres strategy roles)

> **Diataxis type:** Reference. The source of truth for the five `ceres-*` strategy roles available via Claude Code subagent dispatch. Defined in commit chain `4ba11e2` → 9.5k (see `docs/superpowers/specs/2026-05-28-stage-9-5k-codified-subagents-design.md`).

## Why these exist

Multi-perspective agent passes that scope strategy work were dispatched without a read-first contract. The 4-agent confirmation pass that approved Stage 9.5a/9.5c missed the existing `Every_controller_action_declares_authorization_intent` architecture test — none of the agents was instructed to read the codebase before answering. CER003 was withdrawn during planning as a result. These five roles codify "read the codebase before reasoning" as the role's identity, not as a per-dispatch reminder.

## The five roles

| Role (`subagent_type`) | Stance | Always-read baseline | Tools | Dispatch when... |
|---|---|---|---|---|
| `ceres-architect` | System-wide design trade-offs, layer boundaries, cross-cutting impact | `CLAUDE.md`, `docs/architecture.md`, `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`, the most-recent spec under `docs/superpowers/specs/` | `Read, Grep, Glob, WebFetch` | A decision spans multiple subsystems or could conflict with an existing convention |
| `ceres-tech-lead` | Implementation feasibility, existing source patterns, effort estimate | `CLAUDE.md`, `docs/testing.md`, the active roadmap stage section, the affected source directory (dispatcher-named) | `Read, Grep, Glob, Bash` (read-only) | A decision needs an effort estimate, an existing-pattern check, or a feasibility sanity-check |
| `ceres-pm` | User-facing implications, scope-vs-sprint fit, planning-doc alignment | `CLAUDE.md`, `docs/planning-phase3.md`, `docs/security-model.md`, the active roadmap stage section | `Read, Grep, Glob` | A decision needs a scope-vs-sprint fit check, a user-facing read, or a planning-doc alignment check |
| `ceres-cto` | Top-level constraints, trip-wires, phase discipline, autonomy posture | `CLAUDE.md`, `.claude/skills/playbook/references/constitution.md`, `docs/roadmap-phase-three.md`, recent ADRs under `docs/decisions/` | `Read, Grep, Glob, WebFetch` | A decision touches phase discipline, autonomy posture, trip-wires, or the playbook constitution |
| `ceres-security-reviewer` | Auth / RLS / pre-auth-scope / token-lookup / threat surface | `CLAUDE.md`, `docs/security-model.md`, `docs/multi-tenancy-strategy.md`, `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`, affected `ProjectCeres/Common/Authentication/` files | `Read, Grep, Glob, WebFetch` | A decision touches authentication, multi-tenancy, RLS, pre-auth scope, token lookup, or any IUserOwned entity |

## The contract

Every `ceres-*` response opens with two sections in this order:

```
## What I read
- <path> — <one line: what you looked for in it>
- ... (every baseline file + every dispatcher-named file)

## Conflicts found
- <file:line> — <existing convention / test / ADR / pattern that the proposed work would conflict with or override>
- (or: "None. Checked: <the specific absences you verified>")

## {then the strategy answer}
```

The role file's body carries this contract verbatim. It is **advisory** (the platform cannot bind a system-prompt instruction). Enforcement lives in the dispatcher.

## The dispatcher gate

When the orchestrating agent dispatches a `ceres-*` strategy role, it inspects the response for both preamble sections before synthesizing from the output. If either is missing, OR if the `## What I read` list does not include the role's baseline files plus the files named in the dispatch prompt, the dispatcher re-dispatches with a stricter prompt. The dispatcher does not build on a `ceres-*` response that skipped the read step.

This gate is codified in `CLAUDE.md` § "Using subagents".

## What these roles do NOT do

- They do not mutate code. The `tools` allowlist excludes `Edit`, `Write`, and (for all but `ceres-tech-lead`) `Bash`. `ceres-tech-lead`'s `Bash` is read-only by role-description contract.
- They are not the 9.5e review-pipeline roles (`writer` / `security` / `playwright-test-audit`). Those audit a finished diff and ship separately under 9.5e with a diff-focused read-list.
- They do not spawn other subagents (platform does not support subagent recursion).

## Gotchas (per claude-code-guide research, 2026-05-28)

- Editing a role file on disk requires a session restart to load — or use the `/agents` UI which takes effect immediately.
- Tool-name typos in the `tools` frontmatter silently grant ALL tools. Validate via `/agents` after any edit.
- Identity resolves via the `name:` frontmatter field, not the filename. Duplicate `name` values are silently discarded.
- Project-scope `.claude/agents/` wins over user-scope `~/.claude/agents/`.

## Cross-references

- **Spec:** `docs/superpowers/specs/2026-05-28-stage-9-5k-codified-subagents-design.md`
- **Plan:** `docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md`
- **Motivating incident:** the CER003 withdrawal during 9.5c planning — see `docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md` § *Alternatives considered* → Alternative 1.
- **Roadmap entry:** `docs/roadmap-phase-three.md` § Stage 9.5h sub-stage 9.5k.
```

- [ ] **Step 2: Verify the file**

Run: `head -3 docs/agents.md && echo --- && wc -l docs/agents.md`
Expected: heading matches; line count ~60 ±5.

## Task 7: Insert CLAUDE.md § "Using subagents"

**Files:**
- Modify: `CLAUDE.md` (insert new section between line 130 and line 132 — i.e., after the closing of § "Frontend Work", before the heading of § "After Completing Any Stage")

- [ ] **Step 1: Confirm the insertion anchor exists**

Run: `grep -n "^## After Completing Any Stage" CLAUDE.md`
Expected: `132:## After Completing Any Stage` (line number may drift ±2 if the file has been edited since 2026-05-28; the anchor is the section heading, not the line number).

- [ ] **Step 2: Apply the edit**

Use the Edit tool with this exact replacement.

**`old_string`** (the existing heading line that comes RIGHT AFTER the new section's insertion point):
```
## After Completing Any Stage
```

**`new_string`** (the new section + the existing heading line, preserved verbatim):
```
## Using subagents

Five codified `ceres-*` strategy roles ship at `.claude/agents/ceres-{architect,tech-lead,pm,cto,security-reviewer}.md`. Dispatch by `subagent_type` during brainstorm/spec/plan when a decision needs an outside perspective with a read-first contract. The five role files + dispatch guidance live in `docs/agents.md`.

**The dispatcher-gate rule (binding on me, the orchestrator):** when I dispatch a `ceres-*` strategy agent, I MUST inspect its response for both preamble sections (`## What I read`, `## Conflicts found`) before synthesizing from its output. If either is missing, OR if the `## What I read` list does not include the role's baseline files plus the files I named in the dispatch prompt, I re-dispatch with a stricter prompt. I do not build on a `ceres-*` response that skipped the read step.

This is the gate the PreToolUse hook cannot be: a strategy agent's deliverable is text, and a PreToolUse hook can only deny tool calls. The dispatcher is the enforcement layer.

## After Completing Any Stage
```

- [ ] **Step 3: Verify the edit**

Run: `grep -n "^## Using subagents\|^## After Completing Any Stage\|^## Frontend Work" CLAUDE.md`
Expected output (line numbers may drift ±5):
```
120:## Frontend Work
132:## Using subagents
140:## After Completing Any Stage
```

(The `## Using subagents` line should sit between `## Frontend Work` and `## After Completing Any Stage`. If the order is wrong, undo and re-apply.)

Also: `wc -l CLAUDE.md` — expected to grow by ~9 lines (the new section is 8 content lines + 1 blank).

## Task 8: Tick the 9.5k roadmap row + crisp-up Trip-wire C

**Files:**
- Modify: `docs/roadmap-phase-three.md` (line 1253 — the 9.5k `[ ]` row; line 1261 — the batch close-out Trip-wire C parenthetical)

- [ ] **Step 1: Tick the 9.5k row**

Use the Edit tool. The 9.5k checklist line currently starts with `- [ ] 9.5k —`. The whole line is long, so use a unique substring near the start.

**`old_string`**:
```
- [ ] 9.5k — Codified subagent definitions at `.claude/agents/ceres-{architect,tech-lead,pm,cto,security-reviewer}.md`
```

**`new_string`**:
```
- [x] 9.5k — Codified subagent definitions at `.claude/agents/ceres-{architect,tech-lead,pm,cto,security-reviewer}.md`
```

(This flips just the `[ ]` → `[x]`; the rest of the line — the description, smoke-test guidance, cross-references — is preserved verbatim.)

- [ ] **Step 2: Crisp-up Trip-wire C in the batch close-out line**

Use the Edit tool.

**`old_string`**:
```
hook stack still ≤10 (Trip-wire C)
```

**`new_string`**:
```
Stop-event hooks ≤10 (Trip-wire C; currently 3)
```

This resolves the ambiguity flagged in spec §11 action 1 (the "hook stack ≤10" reading nearly blocked the binding-hook decision during 9.5k brainstorm because the total hook count is ~26 while Stop-event hooks specifically are 3).

- [ ] **Step 3: Verify both edits**

Run:
```
grep -n "9.5k — Codified subagent" docs/roadmap-phase-three.md
grep -n "Stop-event hooks ≤10\|hook stack still ≤10" docs/roadmap-phase-three.md
```

Expected:
- The first grep returns two lines (the table row on ~1240, the checklist row on ~1253). The 1253 row should now start with `- [x] 9.5k —`.
- The second grep returns one line containing `Stop-event hooks ≤10 (Trip-wire C; currently 3)`, NOT the old `hook stack still ≤10` text.

If either expected match is missing, undo via `git diff` inspection + Edit re-apply.

## Task 9: Verify the diff before staging

- [ ] **Step 1: Inspect git status**

Run: `git status --short`
Expected output (path order may vary):
```
 M CLAUDE.md
 M docs/roadmap-phase-three.md
?? .claude/agents/
?? docs/agents.md
```

- [ ] **Step 2: Confirm all 5 agent files are present**

Run: `ls -1 .claude/agents/`
Expected:
```
ceres-architect.md
ceres-cto.md
ceres-pm.md
ceres-security-reviewer.md
ceres-tech-lead.md
```

(Order is alphabetical from `ls -1`.)

- [ ] **Step 3: Confirm each agent file's frontmatter `name:` matches the spec K6 self-contained constraint**

Run: `grep -H "^name:" .claude/agents/*.md`
Expected:
```
.claude/agents/ceres-architect.md:name: ceres-architect
.claude/agents/ceres-cto.md:name: ceres-cto
.claude/agents/ceres-pm.md:name: ceres-pm
.claude/agents/ceres-security-reviewer.md:name: ceres-security-reviewer
.claude/agents/ceres-tech-lead.md:name: ceres-tech-lead
```

If any `name:` value drifts from this expected list, fix it (the platform resolves by `name:`, not filename — a typo here silently disables the role).

- [ ] **Step 4: Confirm each agent file's `tools` allowlist matches the spec §5.3 table**

Run: `grep -H "^tools:" .claude/agents/*.md`
Expected:
```
.claude/agents/ceres-architect.md:tools: Read, Grep, Glob, WebFetch
.claude/agents/ceres-cto.md:tools: Read, Grep, Glob, WebFetch
.claude/agents/ceres-pm.md:tools: Read, Grep, Glob
.claude/agents/ceres-security-reviewer.md:tools: Read, Grep, Glob, WebFetch
.claude/agents/ceres-tech-lead.md:tools: Read, Grep, Glob, Bash
```

Tool-name typos in this field silently grant ALL tools (spec §3 research gotcha). If any value drifts, fix it.

- [ ] **Step 5: Confirm each agent file's body carries the `## What I read` + `## Conflicts found` contract**

Run: `grep -lc "## What I read" .claude/agents/*.md && echo --- && grep -lc "## Conflicts found" .claude/agents/*.md`
Expected: each grep lists all 5 files (one match per file).

If any file is missing either heading, the read-first contract is not present in that role — re-apply the body template.

## Task 10: Commit

- [ ] **Step 1: Stage all changes**

Run:
```
git add .claude/agents/ docs/agents.md CLAUDE.md docs/roadmap-phase-three.md
```

- [ ] **Step 2: Verify the staged diff covers exactly what was planned**

Run: `git diff --cached --stat`
Expected: 8 files changed —
- 5 created under `.claude/agents/`
- 1 created at `docs/agents.md`
- 1 modified `CLAUDE.md` (~+9 lines)
- 1 modified `docs/roadmap-phase-three.md` (~+2 lines, ~−2 lines from the two Edits)

If extra files appear, inspect with `git status` and unstage anything unrelated.

- [ ] **Step 3: Commit**

Run:
```
git commit -m "$(cat <<'EOF'
feat(agents): Stage 9.5k codified ceres-* subagent definitions

Ships 5 strategy-role agent files at .claude/agents/ceres-{architect,
tech-lead,pm,cto,security-reviewer}.md, each carrying a baseline
read-list and the ## What I read / ## Conflicts found response
contract per spec K1-K6.

Adds docs/agents.md (discoverability reference, Diataxis: Reference)
and CLAUDE.md § Using subagents (the dispatcher-gate rule that
codifies enforcement, since a PreToolUse hook cannot gate text-only
agent output per spec K5).

Ticks the 9.5k roadmap row and crisps up Trip-wire C in the batch
close-out line from the ambiguous "hook stack ≤10" to "Stop-event
hooks ≤10 (currently 3)" per spec §11 action item 1.

Smoke-test gated by session restart (agent files load at session
start, per claude-code-guide gotcha) — captured as a follow-up
task in the TaskList, not in this commit.

Spec: docs/superpowers/specs/2026-05-28-stage-9-5k-codified-subagents-design.md
Plan: docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Verify the commit landed**

Run: `git log -1 --stat`
Expected: 8 files in the stat (5 agents + docs/agents.md + CLAUDE.md + roadmap), commit message intact, Co-Authored-By trailer present.

Also: `git status` — expected `nothing to commit, working tree clean` (or only unrelated files in the working tree).

## Task 11: Smoke-test the 5 roles (REQUIRES SESSION RESTART)

> **Cannot run in the same session that authored the files.** Per spec §8 + claude-code-guide research gotcha: agent files load at session start; editing/creating on disk does not take effect mid-session. The `/agents` UI takes effect immediately, but we want to validate the on-disk-load path that real future dispatches will use.

- [ ] **Step 1: Capture the in-session pre-restart marker**

After Task 10 completes, the dispatcher (you, the orchestrating agent) should END THE TURN and tell the user:

> Stage 9.5k shipped at commit `<SHA>`. The 5 agent files require a session restart to load. After restart, the smoke-test is Task 11 of `docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md` — please `/clear` then start a new session and ask me to run the 9.5k smoke-test.

This is the handoff. Do NOT attempt to smoke-test in-session — the files won't be loaded.

- [ ] **Step 2 (next session): Smoke-test each role**

After the session restart, dispatch each role with the trivial real-Project-Ceres question below. Validate each response against the per-role assertion.

For **`ceres-architect`** — prompt:

> A new `BackupCodeRedemption` audit-log entry: should it live as a new column on `AuditLogEntries` or as a separate `BackupCodeRedemptions` table? Spec context: `docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md`.

**Assert:** the response opens with `## What I read` listing `CLAUDE.md`, `docs/architecture.md`, `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`, and the named spec — then `## Conflicts found` — then the architecture answer.

For **`ceres-tech-lead`** — prompt:

> Adding a `[PreAuthScope]` decoration to a 12th existing service in `ProjectCeres/Common/Authentication/` — what's the smallest patch shape, and which tests need re-running?

**Assert:** the response opens with `## What I read` listing `CLAUDE.md`, `docs/testing.md`, the active 9.5h roadmap section, and the `ProjectCeres/Common/Authentication/` directory listing — then `## Conflicts found` — then the feasibility answer.

For **`ceres-pm`** — prompt:

> Stage 9.5b's user-facing scope is "production-parity DB layer with RLS-parity assertion" — does that surface any user-visible behaviour change, or is it purely infrastructure?

**Assert:** the response opens with `## What I read` listing `CLAUDE.md`, `docs/planning-phase3.md`, `docs/security-model.md`, and the 9.5b roadmap section — then `## Conflicts found` — then the planning answer.

For **`ceres-cto`** — prompt:

> A new Stop-event hook is being proposed for 9.5e — does that respect Trip-wire C's current budget?

**Assert:** the response opens with `## What I read` listing `CLAUDE.md`, the playbook constitution, `docs/roadmap-phase-three.md` (with Trip-wire C cited), and the recent ADRs — then `## Conflicts found` — then the constraints answer. Bonus signal: the answer should reference "Stop-event hooks ≤10 (currently 3)" per the crisp-up landed in Task 8.

For **`ceres-security-reviewer`** — prompt:

> A new pre-auth service is being added to `ProjectCeres/Common/Authentication/`. What's the IUserOwned story for any entity it touches, and what `[RlsBypassJustified]` annotations are likely needed?

**Assert:** the response opens with `## What I read` listing `CLAUDE.md`, `docs/security-model.md`, `docs/multi-tenancy-strategy.md`, `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`, and the named directory — then `## Conflicts found` — then the security-review answer.

- [ ] **Step 3: Capture the 5 transcripts**

Append the 5 dispatched-prompt + response pairs to `docs/agents.md` as a new `## Appendix A — Initial smoke-test transcripts (2026-05-29)` section, or alternatively reference them in the commit message of a follow-up `docs(agents): capture initial smoke-test transcripts` commit.

- [ ] **Step 4: Validate `/agents` UI**

Run `/agents` in the chat. Confirm the 5 `ceres-*` roles appear in the project-scope section with the expected `tools` allowlist. The UI surfaces tool-name typos immediately — if any role shows "all tools" instead of the expected allowlist, the `tools:` frontmatter has a typo that the file-load path silently accepted.

- [ ] **Step 5: If all 5 smoke-tests pass + `/agents` UI shows the correct allowlist, the stage is complete.** Update the TaskList task #78 to `completed`. No additional commit needed unless transcripts were captured per Step 3.

---

## Self-Review

I checked this plan against the spec:

**Spec coverage:**
- §2 K1 (advisory + dispatcher-gate) → Task 7 codifies the gate in CLAUDE.md; the agent bodies in Tasks 1–5 carry the advisory contract.
- §2 K2 (5 strategy roles, separate from 9.5e review roles) → Tasks 1–5 create exactly the 5 roles; Task 6's `docs/agents.md` § "What these roles do NOT do" explicitly separates them from 9.5e.
- §2 K3 (static baseline + dispatcher extras) → each agent file's body lists the static baseline (per §5.3 table) and instructs the role to read dispatcher-named extras.
- §2 K4 (preamble shape) → every agent file body carries the `## What I read` + `## Conflicts found` template verbatim.
- §2 K5 (0 new hooks) → confirmed; no hook file created.
- §2 K6 (self-contained, no inheritance) → each agent file is a full standalone system prompt with the contract repeated; no shared include.
- §5.1 frontmatter → Tasks 1–5 frontmatter matches `name` / `description` / `tools` / `model` per spec.
- §5.2 body template → copied verbatim per role.
- §5.3 baseline read-list per role → matches the table; `ArchitectureTests.cs` is in `ceres-architect` + `ceres-security-reviewer` baselines per the "structurally can't recur" note.
- §5.4 discoverability doc → Task 6.
- §6 dispatcher gate codification → Task 7.
- §8 error handling (session-restart requirement) → Task 11 step 1 documents the handoff.
- §9 smoke-test 5 dispatches → Task 11 step 2.
- §10 rollout (single commit) → Task 10.
- §11 action item 1 (Trip-wire C crisp-up) → Task 8 step 2.
- §11 action item 2 (ArchitectureTests path correction) → already shipped in commit `5652329` (out of this plan's scope, noted in spec §4 pre-flight findings).

**Placeholder scan:** none. Every step has the exact text or command needed.

**Type consistency:** the `name:` values, `tools:` allowlists, baseline paths, and preamble headings are consistent across Tasks 1–5 and Task 6's reference table. The CLAUDE.md insertion in Task 7 is consistent with the dispatcher-gate prose in Task 6's `docs/agents.md`.

No gaps found. Plan is ready.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md`.

This plan is **mechanical text editing** in a single atomic commit. There are no independent tasks to parallelize, no implementation choices to delegate, no test-driven code paths, and no judgment calls that benefit from a fresh-context subagent. Two-stage spec/quality review per task would add ~8x subagent overhead with zero quality lift — the 5 agent files follow one template, and the 3 doc edits are line-targeted.

**Recommendation: Inline Execution via `superpowers:executing-plans`** — run Tasks 1–10 in this session, then handoff Task 11 (smoke-test) to the next session per spec §8.

The two options remain available:

**1. Inline Execution (recommended for this plan)** — Execute Tasks 1–10 in this session using executing-plans, with a checkpoint after Task 9 (pre-commit diff inspection).

**2. Subagent-Driven** — Dispatch a fresh subagent per task with two-stage review. Costs ~8x more subagent calls for the same mechanical work; included as an option for completeness.

Which approach?
