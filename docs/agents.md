# Codified subagents (Project Ceres strategy roles)

> **Diataxis type:** Reference. The source of truth for the five `ceres-*` strategy roles available via Claude Code subagent dispatch. Defined in commit chain `4ba11e2` → 9.5k (see `docs/superpowers/specs/2026-05-28-stage-9-5k-codified-subagents-design.md`).

## Why these exist

Multi-perspective agent passes that scope strategy work were dispatched without a read-first contract. The 4-agent confirmation pass that approved Stage 9.5a/9.5c missed the existing `Every_controller_action_declares_authorization_intent` architecture test — none of the agents was instructed to read the codebase before answering. CER003 was withdrawn during planning as a result. These five roles codify "read the codebase before reasoning" as the role's identity, not as a per-dispatch reminder.

## The five roles

| Role (`subagent_type`) | Stance | Always-read baseline | Tool guard (`disallowedTools`) | Dispatch when... |
|---|---|---|---|---|
| `ceres-architect` | System-wide design trade-offs, layer boundaries, cross-cutting impact | `CLAUDE.md`, `docs/architecture.md`, `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`, the most-recent spec under `docs/superpowers/specs/` | `Write, Edit, NotebookEdit, Bash` | A decision spans multiple subsystems or could conflict with an existing convention |
| `ceres-tech-lead` | Implementation feasibility, existing source patterns, effort estimate | `CLAUDE.md`, `docs/testing.md`, the active roadmap stage section, the affected source directory (dispatcher-named) | `Write, Edit, NotebookEdit` (keeps read-only `Bash`) | A decision needs an effort estimate, an existing-pattern check, or a feasibility sanity-check |
| `ceres-pm` | User-facing implications, scope-vs-sprint fit, planning-doc alignment | `CLAUDE.md`, `docs/planning-phase3.md`, `docs/security-model.md`, the active roadmap stage section | `Write, Edit, NotebookEdit, Bash` | A decision needs a scope-vs-sprint fit check, a user-facing read, or a planning-doc alignment check |
| `ceres-cto` | Top-level constraints, trip-wires, phase discipline, autonomy posture | `CLAUDE.md`, `.claude/skills/playbook/references/constitution.md`, `docs/roadmap-phase-three.md`, recent ADRs under `docs/decisions/` | `Write, Edit, NotebookEdit, Bash` | A decision touches phase discipline, autonomy posture, trip-wires, or the playbook constitution |
| `ceres-security-reviewer` | Auth / RLS / pre-auth-scope / token-lookup / threat surface | `CLAUDE.md`, `docs/security-model.md`, `docs/multi-tenancy-strategy.md`, `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`, affected `ProjectCeres/Common/Authentication/` files | `Write, Edit, NotebookEdit, Bash` | A decision touches authentication, multi-tenancy, RLS, pre-auth scope, token lookup, or any IUserOwned entity |

The roles use a **deny-list** (`disallowedTools`), not an allow-list (`tools`). The role inherits the dispatcher's full read tool set, minus the mutating tools listed. This states the actual invariant — *never mutate code* — directly, survives future read-tool additions without maintenance, and avoids the allow-list typo footgun where a misspelled tool name silently grants all tools. `ceres-tech-lead` omits `Bash` from its deny-list because its role is the only one that runs read-only `Bash` (`dotnet build`, `grep`); its body contract forbids mutating `Bash`. Switched from the original `tools:` allow-list 2026-05-30 after the docs gained a worked `disallowedTools` read-only example (code.claude.com sub-agents page, updated 2026-05-29).

## The contract

Every `ceres-*` response **opens with `## What I read` as its literal first line** — no lead sentence, framing, or thinking-aloud before it — then two sections in this order:

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

When the orchestrating agent dispatches a `ceres-*` strategy role, it inspects the response before synthesizing from the output and re-dispatches with a stricter prompt if any fail: (1) the **first non-whitespace line is the literal `## What I read` heading** (no preamble); (2) both preamble sections are present and ordered (`## What I read` → `## Conflicts found` → answer); (3) the `## What I read` list includes the role's baseline files plus the files named in the dispatch prompt. The dispatcher does not build on a `ceres-*` response that skipped the read step or buried it under preamble. The first-line rule was added 2026-05-30 after the 9.5k smoke-test found 2/5 roles opening with prose before the heading.

This gate is codified in `CLAUDE.md` § "Using subagents".

## What these roles do NOT do

- They do not mutate code. The `disallowedTools` deny-list removes `Write`, `Edit`, `NotebookEdit`, and (for all but `ceres-tech-lead`) `Bash`. `ceres-tech-lead`'s `Bash` is read-only by role-description contract.
- They are not the 9.5e review-pipeline roles (`writer` / `security` / `playwright-test-audit`). Those audit a finished diff and ship separately under 9.5e with a diff-focused read-list.
- They do not spawn other subagents (platform does not support subagent recursion).

## Gotchas (per claude-code-guide research, 2026-05-28; tool-guard finding 2026-05-30)

- Editing a role file on disk requires a session restart to load — or use the `/agents` UI which takes effect immediately.
- A typo in an allow-list `tools` value can silently grant ALL tools rather than erroring (docs don't specify the failure mode). These roles use a `disallowedTools` deny-list to sidestep that footgun: a typo'd deny entry fails open to *more* restriction-attempts, never to all-tools, and the invariant being asserted (never mutate) is stated directly. Validate the effective set via `/agents` after any edit — there is no documented CLI/log way to audit it otherwise.
- Composition order (per current docs): inherited tools → `disallowedTools` removed → `tools` allow-list applied if present. These roles set only `disallowedTools`, so they inherit-all-minus-mutating.
- Identity resolves via the `name:` frontmatter field, not the filename. Duplicate `name` values are silently discarded.
- Project-scope `.claude/agents/` wins over user-scope `~/.claude/agents/`.

## Cross-references

- **Spec:** `docs/superpowers/specs/2026-05-28-stage-9-5k-codified-subagents-design.md`
- **Plan:** `docs/superpowers/plans/2026-05-28-stage-9-5k-codified-subagents-impl.md`
- **Motivating incident:** the CER003 withdrawal during 9.5c planning — see `docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md` § *Alternatives considered* → Alternative 1.
- **Roadmap entry:** `docs/roadmap-phase-three.md` § Stage 9.5h sub-stage 9.5k.
