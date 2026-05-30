# Stage 9.5k — Codified subagent definitions (read-first contract)

> **Diataxis type:** Reference + explanation — design spec for sub-stage 9.5k under `## Stage 9.5h — Phase 1 hardening container` in `docs/roadmap-phase-three.md`.
>
> **Status:** Draft — design approved 2026-05-28. Awaiting implementation plan + user review of this spec.
>
> **Predecessor:** 9.5c (analyzers, shipped 2026-05-27 at warning severity). **Successor:** 9.5b (DualContextWebApplicationFactory) — which is a prerequisite-consumer of 9.5k.

## 1. Problem

Two failures this session traced to the same root cause: multi-agent / subagent passes operating without guardrails.

1. **CER003 miss (2026-05-26).** The 3-agent + 4-agent perspective passes that scoped 9.5a/9.5c synthesised plausible strategy WITHOUT reading the codebase. None of the dispatched agents was instructed to read existing tests or conventions before answering. The passes missed an existing architecture test (`ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs::Every_controller_action_declares_authorization_intent`) that conflicts with the proposed CER003 class-level `[Authorize]` analyzer. The conflict surfaced only when the implementation-planning step forced a controller-file read — by which point CER003 was already in the spec, the roadmap, and the conditions.

2. **Lost-roadmap-edits (2026-05-27).** A subagent's git operation silently reverted controller-side unstaged doc edits (the Stage 9.5h container + sub-stage rows). The edits had to be rebuilt. Root cause: subagents have full tool access including `git`; controller-side unstaged edits are not safe across a subagent dispatch.

The common thread: **subagents are dispatched as throwaway "pretend you're an architect" prompts with no permanent stance, no read-first contract, and no dispatcher-side verification of what they actually read.** The fix is to codify the roles so the "read the codebase before reasoning" contract is structural, and to make the dispatcher's verification of that contract a hard rule.

## 2. Locked decisions

| Lock | Decision | Source |
|---|---|---|
| K1 | Enforcement is **advisory codification + dispatcher-side verification**, NOT a binding hook. The agent body carries the read-first contract; the dispatcher (the orchestrating agent) inspects every `ceres-*` response for the required preamble and re-dispatches if it's missing or incomplete. | User Q (2026-05-28 brainstorm) + claude-code-guide research finding |
| K2 | **5 strategy roles** shipped in 9.5k: `ceres-architect`, `ceres-tech-lead`, `ceres-pm`, `ceres-cto`, `ceres-security-reviewer`. 9.5e's 3 review roles (writer / security / playwright-test-audit) stay separate, shipped in 9.5e with a diff-focused read-list. | User Q (2026-05-28 brainstorm) |
| K3 | Read-list shape: **static named-path baseline + dispatcher-supplied question-specific extras.** Each role names a 3–5 path always-read floor; the dispatcher adds question-specific files in the dispatch prompt. | User Q (2026-05-28 brainstorm) |
| K4 | Response-shape contract: every `ceres-*` response opens with `## What I read` then `## Conflicts found` then the strategy answer. The dispatcher's verification of this preamble is the gate. | User Q (2026-05-28 brainstorm) |
| K5 | **0 new hooks.** The PreToolUse-can't-block-text limitation (a hook can gate tool calls but not a text-only deliverable) means a hook can't enforce read-first on a strategy agent whose output is text. The dispatcher is the gate instead. | claude-code-guide research finding |
| K6 | Self-contained files, **no inheritance** (the platform doesn't support it; the body fully replaces the default system prompt). The read-first contract text is copied verbatim into each of the 5 files. | claude-code-guide research finding |

## 3. Research findings (claude-code-guide, citing code.claude.com/docs/en/sub-agents.md)

- The `.claude/agents/<name>.md` body becomes the subagent's **full system prompt** (it replaces, not appends-to, the default). It is **advisory** — an instruction like "read X before answering" is a suggestion, not a binding directive. Binding enforcement requires a PreToolUse hook, which can only gate tool calls (not text-only output).
- Identity resolves via the `name:` frontmatter field (kebab-case, lowercase + hyphens), NOT the filename. Duplicate `name` values are silently discarded.
- Frontmatter: `name` + `description` required; `tools` (allowlist, comma-separated or YAML array), `model` (`inherit` default), `disallowedTools` optional. Omitting `tools` inherits ALL tools.
- `description` drives heuristic auto-delegation. `model: inherit` lets the dispatcher pick per-invocation.
- Project-scope `.claude/agents/` wins over user-scope `~/.claude/agents/`.
- **Gotcha:** tool-name typos in the `tools` field can silently grant all tools. Validate via the `/agents` interface.
- **Gotcha:** files load at session start; editing on disk requires a session restart to take effect (the `/agents` UI takes effect immediately).
- Subagents cannot spawn other subagents (no recursion).

## 4. Pre-flight findings (verify-against-codebase, 2026-05-28)

- `.claude/agents/` does NOT exist — 9.5k creates it (greenfield).
- `docs/agents.md` does NOT exist — 9.5k creates it.
- All 7 baseline read-list docs confirmed present at cited paths.
- **CORRECTION:** the architecture-test file is at `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`, NOT `ProjectCeres.Tests/Unit/Architecture/`. The earlier CER003 work mis-cited the path in 3 docs (`architecture.md`, `ADR-0077`, the 9.5c spec); corrected in the same commit chain as this spec.
- CLAUDE.md § Frontend Work routes `ProjectCeres.Client/` changes through the `frontend-orchestrator` skill — a skill-routing convention, NOT a subagent-dispatch convention. No conflict; 9.5k's new § "Using subagents" is complementary.
- No existing `.claude/agents/` precedent — 9.5k is the first. Frontmatter follows the official docs format.

## 5. Architecture

Five self-contained markdown files at `.claude/agents/ceres-{architect,tech-lead,pm,cto,security-reviewer}.md`. No runtime artifacts, no hooks, no state files — pure configuration loaded at session start. Plus one discoverability doc (`docs/agents.md`) and one CLAUDE.md section.

### 5.1 Frontmatter (per role)

> **AMENDED 2026-05-30 (supersedes the `tools:` allow-list below).** The tool guard switched from a `tools:` allow-list to a `disallowedTools:` deny-list after the 9.5k smoke-test + follow-up research (claude-code-guide, citing the code.claude.com sub-agents page updated 2026-05-29, which gained a worked `disallowedTools` read-only example the spec's original 2026-05-28 research predated). Rationale: (1) the allow-list carries a typo footgun — a misspelled tool name can silently grant ALL tools; (2) a deny-list states the actual invariant (*never mutate code*) directly and survives future read-tool additions without maintenance. Current frontmatter is `disallowedTools: Write, Edit, NotebookEdit, Bash` for the four read-only roles, and `disallowedTools: Write, Edit, NotebookEdit` for `ceres-tech-lead` (which keeps read-only `Bash`). See `docs/agents.md` for the live per-role table. The allow-list spec text below is retained for decision history.

```yaml
---
name: ceres-architect
description: System-architecture perspective for Project Ceres design decisions. Reads the codebase BEFORE answering. Use during brainstorm/spec/plan when a decision spans multiple subsystems or could conflict with an existing convention.
disallowedTools: Write, Edit, NotebookEdit, Bash
model: inherit
---
```

- `name`: kebab-case, `ceres-` prefix.
- `description`: states the stance + the read-first behaviour + when to dispatch (drives auto-delegation + tells future-me which role fits).
- `disallowedTools`: **deny-list of mutating tools** (`Write`, `Edit`, `NotebookEdit`, `Bash`). Strategists scope; they don't implement. The role inherits the dispatcher's full read tool set minus these. Exception: `ceres-tech-lead` omits `Bash` from its deny-list for read-only `dotnet build` / `grep` feasibility checks (documented inline as read-only-intended; the role description forbids mutation). ~~Original lock: a `tools:` allow-list (`Read, Grep, Glob, WebFetch`); see the 2026-05-30 amendment above.~~
- `model: inherit` — the dispatcher picks the model per-invocation.

### 5.2 Body (the system prompt — copied structure per role)

```
You are the {ROLE} perspective for Project Ceres. {1-2 sentence stance.}

## BEFORE YOU ANSWER — read first (non-negotiable)

Read these baseline files every time, regardless of the question:
{the role's named-path baseline}

Then read any additional files the dispatcher named in the prompt.

## Your response MUST open with these two sections, in this order:

## What I read
- <path> — <one line: what you looked for in it>
- ... (every baseline file + every dispatcher-named file)

## Conflicts found
- <file:line> — <existing convention / test / ADR / pattern that the
  proposed work would conflict with or override>
- (or: "None. Checked: <the specific absences you verified>")

## {then your strategy answer}

If you answer without the two preamble sections, or your "What I read"
list is missing a baseline file, the dispatcher will re-dispatch you
with a stricter prompt. Your job is to be RIGHT about Project Ceres as
it actually is — not to be fast or to produce plausible-sounding
strategy from generic priors. The CER003 miss (2026-05-26) happened
because a perspective pass reasoned without reading; this contract exists
to prevent the recurrence.
```

### 5.3 The five roles + named-path baselines (per K3)

| Role | Stance (≤2 sentences) | Always-read baseline | `tools` |
|---|---|---|---|
| **ceres-architect** | System-wide design trade-offs, layer boundaries, cross-cutting impact. | `CLAUDE.md`, `docs/architecture.md`, `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`, the most-recent spec under `docs/superpowers/specs/` for the active stage | `Read, Grep, Glob, WebFetch` |
| **ceres-tech-lead** | Implementation feasibility, existing source patterns, effort estimate. | `CLAUDE.md`, `docs/testing.md`, the active roadmap stage section in `docs/roadmap-phase-three.md`, the affected source directory (dispatcher-named) | `Read, Grep, Glob, Bash` (read-only) |
| **ceres-pm** | User-facing implications, scope-vs-sprint fit, planning-doc alignment. | `CLAUDE.md`, `docs/planning-phase3.md`, `docs/security-model.md`, the active roadmap stage section | `Read, Grep, Glob` |
| **ceres-cto** | Top-level constraints, trip-wires, phase discipline, autonomy posture. | `CLAUDE.md`, `.claude/skills/playbook/references/constitution.md`, `docs/roadmap-phase-three.md` (active stage + trip-wire definitions), the recent ADRs under `docs/decisions/` | `Read, Grep, Glob, WebFetch` |
| **ceres-security-reviewer** | Auth / RLS / pre-auth-scope / token-lookup / threat surface. | `CLAUDE.md`, `docs/security-model.md`, `docs/multi-tenancy-strategy.md`, `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`, the affected `ProjectCeres/Common/Authentication/` files (dispatcher-named) | `Read, Grep, Glob, WebFetch` |

`ArchitectureTests.cs` is in BOTH ceres-architect and ceres-security-reviewer baselines — the specific CER003 miss structurally can't recur.

> The rightmost column above shows the **original `tools:` allow-list** values (decision history). Per the 2026-05-30 amendment in §5.1, the live tool guard is a `disallowedTools:` deny-list — see `docs/agents.md` for the current per-role values.

### 5.4 Discoverability doc — `docs/agents.md`

Diataxis: Reference. Lists all 5 roles in a table: role name, stance, baseline read-list, `tools`, and "dispatch this when..." guidance. Cross-referenced from a new CLAUDE.md § "Using subagents" (≤8 lines) that states the dispatcher-gate rule: *when dispatching a `ceres-*` strategy agent, inspect the response for the `## What I read` + `## Conflicts found` preamble before using its output; re-dispatch if missing.*

## 6. The dispatcher gate (where enforcement actually lives)

Per K1 + K4 + K5: the agent body is the **contract**; the dispatcher is the **gate**. The binding rule, codified in CLAUDE.md § Using subagents:

> When I dispatch a `ceres-*` strategy agent, I MUST inspect its response for both preamble sections (`## What I read`, `## Conflicts found`) before synthesizing from its output. If either is missing, OR if the `## What I read` list does not include the role's baseline files plus the files I named in the dispatch prompt, I re-dispatch with a stricter prompt. I do not build on a `ceres-*` response that skipped the read step.

This is the gate the PreToolUse hook cannot be: a strategy agent's deliverable is text, and a PreToolUse hook can only deny tool calls, not text output. Moving the gate to the dispatcher closes that gap with zero new hooks.

## 7. Data flow

Compile-time-equivalent (config-only). The 5 files load at session start; `Agent(subagent_type: "ceres-architect", prompt: "...")` resolves to the file via its `name`. The agent reads its baseline ∪ dispatcher-named files, emits the preamble + answer. The dispatcher reads the response, verifies the preamble, proceeds or re-dispatches. No persistence, no hooks, no state.

## 8. Error handling

- **Missing preamble:** dispatcher re-dispatches (the gate). Not a silent acceptance.
- **Tool-name typo in frontmatter:** a typo in an allow-list `tools` value can silently grant all tools (research gotcha). The 2026-05-30 amendment (§5.1) moves to a `disallowedTools` deny-list, which fails toward *more* restriction-attempts rather than all-tools. Still validate via `/agents` after any edit — there is no documented CLI/log audit of the effective set.
- **Session-restart requirement:** editing a `.claude/agents/` file on disk requires a session restart to load (research gotcha). The plan notes this; the smoke-test runs after a restart (or via the `/agents` UI which takes effect immediately).

## 9. Testing / verification

- **Smoke-test per role (5 dispatches):** dispatch each role with a trivial real Project Ceres question (e.g. ceres-architect → "should new repository methods take `Guid` or `string` for userId?"). Assert the response opens with `## What I read` listing the role's baseline, then `## Conflicts found`, then the answer. Capture the 5 transcripts in the stage close-out commit message or a `docs/agents.md` appendix.
- **Discoverability check:** `docs/agents.md` lists all 5 + baselines + dispatch guidance; CLAUDE.md § Using subagents cross-references it and states the dispatcher-gate rule.
- **Frontmatter validation:** confirm each file's `disallowedTools` field has no typos (validate via `/agents` interface per research gotcha).
- **Smoke-test executed 2026-05-30:** 5/5 roles passed the dispatcher gate (each emitted `## What I read` + `## Conflicts found` + answer in order, all baselines listed, no mutating-tool use). Run via a pipelined dispatch+verify workflow. Two non-fatal observations: a lead sentence before the `## What I read` heading on 2/5 (architect, security-reviewer); and the agents surfaced real doc-drift they read (the `UserOwnedTables.All` `(8)`→`(9)` count comment, fixed same session).
- **No `dotnet`/`pnpm` test impact** — `.claude/` config + docs only; Stop-hook tier 0 (no .NET-impacting writes).

## 10. Rollout

Single commit (no soak, no chain — config + docs):
1. Create `.claude/agents/ceres-{architect,tech-lead,pm,cto,security-reviewer}.md` (5 files).
2. Create `docs/agents.md`.
3. Add CLAUDE.md § "Using subagents" with the dispatcher-gate rule.
4. Edit `docs/roadmap-phase-three.md`: tick the 9.5k `[ ]` → `[x]`, AND crisp-up the Trip-wire C definition (per §11 action item).
5. Smoke-test the 5 roles (requires session restart to load the files; transcripts captured).

## 11. Action items folded in (per `feedback_no_flag_without_action`)

1. **Crisp-up Trip-wire C.** The roadmap's "hook stack ≤10" is ambiguous — the actual stack is 26 total. Confirmed 2026-05-28 that Trip-wire C counts **Stop-event hooks only** (currently 3). 9.5k's close-out edits the roadmap (Stage 9.5h batch-close-out line + any Trip-wire C reference) to read "Stop-event hooks ≤10 (currently 3)" so the ambiguity that nearly blocked the binding-hook decision doesn't recur.
2. **ArchitectureTests.cs path correction.** 3 docs mis-cited `ProjectCeres.Tests/Unit/Architecture/` (correct: `ProjectCeres.Tests/Integration/Authentication/`). Corrected in the same commit chain as this spec.

## 12. Out of 9.5k — scope-adjacent work routed to receiving stages

| Item | Receiving structure | Why not in 9.5k |
|---|---|---|
| 9.5e's 3 review roles (writer / security / playwright-test-audit) | Stage 9.5h sub-stage 9.5e | Per K2: review roles audit a finished diff with a diff-focused read-list (the diff + the spec it claims to implement + the Playwright trace) — a different job from strategy roles. Shipped with 9.5e. |
| Binding PreToolUse hook for `ceres-*` agents that DO mutate code | Trip-wire-gated follow-up (no `[ ]` opened yet — only opens if advisory proves insufficient) | Per K1 + K5: the dispatcher gate covers the strategy-agent case (text output). A binding hook would only help an agent that goes on to Edit/Write — none of the 5 strategy roles can (their `disallowedTools` deny-list removes `Write`/`Edit`/`NotebookEdit`/`Bash`; see §5.1 amendment — originally a `tools` allow-list). If a future role needs mutation AND read-first enforcement, that's when the hook opens. Documented here so the option isn't lost; not opened as a `[ ]` because there's no current bug it fixes. |

## 13. Open questions

None remaining. All brainstorm decisions locked in §2.

## 14. Cross-references

- **Roadmap:** `docs/roadmap-phase-three.md` § Stage 9.5h sub-stage 9.5k.
- **Predecessor ADR (informs the read-first lesson):** `docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md` — the CER003 withdrawal is the motivating incident.
- **The architecture test that CER003 conflicted with:** `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs::Every_controller_action_declares_authorization_intent`.
- **Constitution (Phase A″ + trip-wires):** `.claude/skills/playbook/references/constitution.md`.
- **Feedback memories:** `feedback_research_before_confident_claims` (the read-first contract is this rule applied to subagents), `feedback_no_flag_without_action` (the two §11 action items), `feedback_trust_bash_output_not_narration` (the lost-roadmap-edits incident is this rule's failure mode at the git-state surface).
