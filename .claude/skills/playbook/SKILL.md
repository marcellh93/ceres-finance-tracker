---
name: playbook
description: Use when starting a stage, finishing a stage, about to commit, about to write a spec, or any time the session has crossed a phase boundary. Routes the existing skill bank into chains per references/constitution.md. The constitution is the source of truth — this skill is the entrypoint that reads it. Hard-blocks at four phases (pre-spec-write, pre-deferral, pre-stage-close, pre-commit) and advises at three (stage-start, mid-build, pre-PR-review).
---

# playbook

## 1 — Why this skill exists

Project Ceres has 25 skills (7 project-scoped, 14 superpowers, 4 slash commands) and a stack of memory files documenting hand-off failures across them. An audit of three real-work sessions (2026-05-11, 2026-05-13, 2026-05-16) found **11 distinct missed-fire incidents** plus 3 partial fires — moments where a skill SHOULD have fired and did not, with measurable cost in user time. The skills themselves work; what's missing is the orchestrator that routes between them at phase boundaries.

`playbook` is that orchestrator. It does not replace any existing skill. It enforces the chain.

## 2 — How it works

Three pieces:

1. **The constitution** at `references/constitution.md` defines seven phases (stage-start → pre-spec-write → mid-build → pre-deferral → pre-stage-close → pre-commit → pre-PR-review) with required chains, gating events, and enforcement levels.
2. **Hooks** at `hooks/*.js` enforce four phases at HARD (PreToolUse `decision: "deny"`) and three at ADVISORY (UserPromptSubmit `additionalContext`).
3. **Per-session state** at `.claude/state/playbook/<session_id>.json` records every `Skill` tool invocation this session. Every HARD gate reads this file to know what's already fired.

## 3 — When this skill fires

- **Auto via hooks** — the four HARD gates fire on PreToolUse boundaries (spec writes, deferrals, stage close-outs, code commits); the three ADVISORY detectors fire on UserPromptSubmit.
- **Manual** — invoke `Skill name=playbook` to read the constitution + current state and print the per-session chain-status table (see §5).
- **Slash command** — `/playbook` is the user-facing entrypoint; same effect as invoking the skill directly.
- **Per-prompt advisory passes** — `stage-start-detect.js`, the existing `decision-detect.js` / `frustration-detect.js` / `pushback-detect.js` / `pre-write-deferral.js` continue firing as today. `playbook` is the umbrella, not a replacement.

## 4 — The eight phases

See `references/constitution.md`. The constitution is the single source of truth — do NOT duplicate it here (per `feedback_pointers_are_not_redundant`). One-line summaries for quick reference:

- **Phase A — stage-start** (advisory): `superpowers:brainstorming` before any plan-mode plan or code Write.
- **Phase B — pre-spec-write** (HARD): `superpowers:brainstorming` + `verify-against-codebase` before a new spec under `docs/superpowers/specs/`.
- **Phase C — mid-build** (advisory + HARD claim-gates): `deep-fix-mode` / `decision-mode` / `no-unjustified-deferrals` fire continuously via existing hooks; `verify-runtime-state` HARD-blocks the Stop event on runtime-state claims that lack evidence (added 2026-05-18); `fix-interaction` HARD-blocks the Stop event when a fix is mentioned without an Edit/Write in the same turn, forcing an explicit "fix or document?" interaction (added 2026-05-20).
- **Phase D — pre-deferral** (HARD): `no-unjustified-deferrals` six-step gate satisfied before deferral language lands in `docs/**`.
- **Phase E — pre-stage-close** (HARD): `sync-docs` + `changelog-sync` fired this session AND zero unchecked `[ ]` items in the closing stage's body.
- **Phase F — pre-commit** (HARD, conditional): `verify-against-codebase` fired since the most recent code Write. Doc-only commits bypass.
- **Phase G — pre-PR-review** (advisory, disabled in Ceres): `superpowers:requesting-code-review` chain; hook lives on disk for future re-enablement.
- **Phase H — pre-handoff** (HARD): `verify-runtime-state` HARD-blocks the Stop event on manual-test handoffs (numbered checklists ≥5 items) that lack a prerequisite-audit block or blocked-step markers on missing-UI steps (added 2026-05-18).

## 5 — Reading the state file

Invoking `Skill name=playbook` (no args) reads `.claude/state/playbook/<session_id>.json` and outputs a compact status table:

```
Phase progress this session:
  ✓ stage-start (brainstorming fired @ tool_use_index=14)
  ✓ pre-spec-write (verify-against-codebase fired @ 27)
  ✓ mid-build (deep-fix-mode auto-fired 1x @ 33; no-unjustified-deferrals 0x)
  ✗ pre-stage-close — sync-docs not fired; changelog-sync not fired; 2 unchecked items in Stage 6.15
  ✗ pre-commit — verify-against-codebase last fired @ 27 but last code Write @ 42 (re-verify needed)
  — pre-PR-review — disabled in Ceres
Open deferrals: 1 (Stage 6.15 in roadmap-phase-three.md, FIXME tripwire)
```

Symbols: `✓` (pass), `✗` (fail/missing), `—` (disabled). No emojis in the output per project Claude.md.

## 6 — Anti-patterns

- **"Just invoke the skill manually and the hooks won't fire."** Wrong — `playbook` records its own state, doesn't bypass gates. The HARD hooks check the state file directly; invoking `playbook` does not satisfy a gate that needs `verify-against-codebase` or `sync-docs`.
- **"Defer the constitution to per-skill frontmatter."** The user explicitly chose embedded over distributed (session `322c64dc`, lines 772–812). Reverting that is a re-architecture, not a polish.
- **"Hard-block every phase."** See incidents 2.1#5 and 2.3#9 in `.claude/skills-cohesion-review.md`: over-blocking frustrates discussion-only turns. The 1C hybrid (4 HARD + 3 ADVISORY) is the deliberate balance.
- **"Bypass the gate by mentioning the skill name in chat."** The `verify-against-codebase` transcript-string bypass is a deliberate carve-out preserved from the prior `require-verify-against-codebase-before-spec.js` behavior — it's not a general escape hatch.

## 7 — Linked memory

- `feedback_brainstorm_spec_plan_execute_flow` — Phase A through Phase G is the literal flow this enforces.
- `feedback_finished_stages_have_no_unchecked_items` — Phase E.
- `feedback_deferral_requires_receiving_stage_checkbox` — Phase D.
- `feedback_defer_work_to_all_three_docs` — Phase D hand-off to `sync-docs`.
- `feedback_sync_docs_before_spa_commits` — Phase F prerequisite that doc-sync runs before SPA-migration commits.
- `feedback_never_skip_tests_to_make_them_pass` — Phase F's test-run state inspection.
- `reference_playbook_skill` — the index entry that surfaces this skill under pressure.

## 8 — Slash command

`/playbook` mirrors `/deep-fix`, `/plain`, `/no-defer`, `/verify` symmetry. The command file lives at `.claude/commands/playbook.md`. Invocation: `/playbook [optional phase name]` — with no argument, prints the full per-session status table; with a phase name (e.g. `/playbook pre-stage-close`), drills into that phase's requirements and current pass/fail state.

## 9 — When NOT to invoke

- The user is mid-conversation, not at a phase boundary — let the existing per-prompt detectors handle it.
- The session has just started and no work has happened yet — there's nothing to report.
- The user explicitly asks to bypass the constitution for a one-off — record the bypass intent and proceed without invoking the skill (do NOT silently overwrite state).
