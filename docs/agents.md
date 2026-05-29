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
