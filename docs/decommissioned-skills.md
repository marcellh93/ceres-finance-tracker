# Decommissioned skills + hooks

Append-only registry of skills and hooks that once existed in `.claude/skills/` or `.claude/hooks/` and have been removed.

Purpose: when an old transcript, memory entry, or playbook reference names a skill or hook that's no longer on disk, this file tells you what happened, when, why, and what replaced it. Keeps the active skill bank lean while preserving institutional knowledge.

**Rule for adding entries:** every deletion of a skill or hook lands one append here in the same commit. Each entry: name, date, removal reason, replacement, where to look for the rule the skill encoded.

---

## fix-interaction (skill + 3 hooks + slash command)

**Removed 2026-05-26.** 9.5a Phase 1 cleanup.

**What was deleted**
- Skill: `.claude/skills/fix-interaction/SKILL.md`, `references/dead-lock-defenses.md`
- Hooks: `detect-fix-mention.js` (Stop), `await-answer.js` (UserPromptSubmit), `check-resolution.js` (Stop)
- State files: `.claude/state/fix-interaction/<session_id>.json`
- Slash command: `/fix-interaction-reset` (`.claude/commands/fix-interaction-reset.md`)
- Env bypass: `CERES_SKIP_FIX_INTERACTION_HOOK=1` no longer respected

**Why removed**

The skill ran a five-state machine (`none → fix-or-document → where → documenting/fixing → resume → none`) over a lexical regex against assistant prose, scanning for "the fix is", "the bug is", "doing it now" without a co-located Edit/Write in the same turn. The Phase 1 audit identified this as a doom-loop layer per Huang et al. (arXiv:2310.01798): self-correction degrades on reasoning tasks. A regex over the same assistant text that produced the violation uses the same context window and same blind spot — false positives that train me to phrase deflections differently rather than stop deflecting.

Origin of removal: 9.5a Phase 1 plan, signed off 2026-05-26. CTO + tech-lead + architect + PM converged on deletion in the round-3 confirmation pass.

**What replaced it**

The `turn-shape.json` bundle slot under `.claude/state/evidence/<stage>/turn-shape.json`. Same enforcement contract (every fix-mention must have a co-located Edit/Write/MultiEdit in the same turn) — but the gate runs against a tool-grounded artifact, not the prose.

- Generator: `.claude/skills/verify-stage-completeness/lib/turn-shape-generator.js`
- Consumer: `.claude/skills/verify-stage-completeness/hooks/evidence-bundle-check.js`

Three claim types captured per turn:
- `fix_mentions[]` — `co_located_edit` flag + `edit_file` path
- `confidence_claims[]` — `co_located_research` flag + `research_tool` + `url`
- `runtime_assertions[]` — `co_located_query` flag + `query_output` path + `tool_used`

Stop is denied if any entry has the `co_located` flag false.

**The discipline didn't change.** The memory rules that codified the behaviour (`feedback_no_flag_without_action`, `feedback_doing_now_requires_tool_call`, `feedback_want_me_to_framing_on_bugs`) remain pinned. The skill was the mechanism; the rules are the rule.

---

## verify-frontend + verify-backend (two siblings, merged back into router)

**Removed 2026-05-26.** 9.5a Phase 1 cleanup.

**What was deleted**
- `.claude/skills/verify-frontend/SKILL.md`
- `.claude/skills/verify-backend/SKILL.md`

**Why removed**

The original `verify-against-codebase` skill was split into the two siblings on 2026-05-17 after the `8c8aab9` ThemeToggle commit ran a full backend audit + `dotnet test` on a four-file `.tsx`-only diff. The split paid off in theory (work proportional to the diff) but added orchestration overhead in practice: a router pointing at two siblings, ~60% duplicate scaffolding per sibling, and the agent occasionally invoked the wrong half.

**What replaced it**

The single `verify-against-codebase` skill at `.claude/skills/verify-against-codebase/SKILL.md` — internally tiered. The skill reads only the backend or frontend audit sections relevant to the proposed artifact's content, or both for mixed/ambiguous artifacts. Same conventions audited, same red flags, fewer files.

**Playbook constitution updated.** Phase B (`pre-spec-write`) and Phase F (`pre-commit`) now require `verify-against-codebase` (one skill, one fire) instead of the tier-table requiring one or both siblings.

---

## Removed Stop-event hooks (lexical-prose layer)

**Removed 2026-05-26.** 9.5a Phase 1 cleanup. Deleted as a class.

| Hook | Lived at | Replacement |
|---|---|---|
| `stop-chat-deferral-detect.js` | `.claude/skills/no-unjustified-deferrals/hooks/` | `turn-shape.json` bundle slot + `pre-write-deferral.js` (still active) |
| `claim-without-research.js` | `.claude/skills/deep-fix-mode/hooks/` | `turn-shape.json` `confidence_claims[]` co-location check |
| `repeat-scope-commits.js` | `.claude/skills/deep-fix-mode/hooks/` | (no direct replacement; cross-turn commit-scope churn observed via `git log` ad-hoc when the user notices) |
| `frustration-detect.js` | `.claude/skills/deep-fix-mode/hooks/` (UserPromptSubmit) | Phrase-based pre-emption removed; `deep-fix-mode` skill still fires on user-typed signals like "ultrathink", "stop going in circles" |
| `stop-runtime-state-claim.js` | `.claude/skills/verify-runtime-state/hooks/` | `turn-shape.json` `runtime_assertions[]` co-location check |
| `stop-manual-test-handoff.js` | `.claude/skills/verify-runtime-state/hooks/` | `agent-walk.ts` manual-test cross-check (every route in a handoff list must appear in the Playwright trace) |
| `stop-roadmap-verify-flip.js` | `.claude/hooks/` | `verify-stage-completeness/hooks/stage-completeness-check.js` (still active, runs on PreToolUse on roadmap edits) |

The class: hooks that read my own outgoing assistant prose and tried to enforce rules by regex over the same context window that produced the violation. Same architectural defect for all of them. Replaced by tool-grounded artifacts in the evidence bundle.

---

## dev-teacher (kept — listed here as a near-miss record)

**NOT removed.** Restored from working-tree state via `git checkout HEAD` after being included in the 9.5a bulk-delete list in error. The 4-agent assessment (2026-05-26) flagged dev-teacher as a personally-attached learning tool that routes session learnings into `docs/guide/` — the agent should never have included it without explicit per-item user confirmation.

**Mitigation (M1):** `.claude/hooks/pre-protected-path-gate.js` is a PreToolUse hook that denies destructive tool calls against directories containing a `.protected` marker file. `dev-teacher/` and `docs/guide/` both carry markers. Bypass requires `CERES_DELETE_PROTECTED=<name>` env var AND the user's most recent message literally naming the path. Structural fix; replaces the memory-only "don't delete this" rule that didn't catch the near-miss.

**Lesson:** future bulk-delete proposals must surface each path with personal-use impact individually for confirmation. The protected-path gate is the structural enforcement; the agent's authoring discipline is the upstream rule.
