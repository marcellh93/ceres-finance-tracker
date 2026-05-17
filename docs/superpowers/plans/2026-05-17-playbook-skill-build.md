# `playbook` skill build — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Each numbered edit becomes its own commit per the user's chosen rollout shape.

**Goal:** Land the 15 edits from the cohesion review at `.claude/skills-cohesion-review.md` to ship the `playbook` skill — a project-scoped orchestrating skill that routes the existing skill bank into chains, hard-blocks irreversible-state phases (spec writes, deferrals, stage close-outs, commits) via PreToolUse hooks, and advises exploratory phases via UserPromptSubmit hooks. Two sidecars (`state-rehydration`, `surface-build-warnings`) ship alongside.

**Architecture (locked):**
- **Name:** `playbook` (project-scoped at `.claude/skills/playbook/`).
- **Enforcement:** 1C hybrid — hard-block PreToolUse hooks for `pre-spec-write` / `pre-deferral` / `pre-stage-close` / `pre-commit`; advisory UserPromptSubmit hooks for `stage-start` / `mid-build` / `pre-PR-review` (disabled in Ceres).
- **Routing matrix:** `playbook/references/constitution.md` — single source of truth, no distributed frontmatter.
- **State tracking:** PostToolUse hook records `Skill` invocations to `.claude/state/playbook/<session_id>.json`; gate hooks read it.
- **Slash command:** `/playbook` mirrors `/deep-fix`, `/plain`, `/no-defer`, `/verify` symmetry.
- **Sidecars:** `state-rehydration` (compaction survival) and `surface-build-warnings.js` (silent regressions like the Tailwind glob in commit `f8616087`).

**Tech stack:** Node.js hooks (Bash interpreter for `.sh`, no external deps), Markdown skill files, JSON state files. No production code touched.

**Source design doc:** `.claude/skills-cohesion-review.md` (this session, 2026-05-17). All open questions resolved in §6 of that file.

---

## Binding constraints

- **Stay on `main`.** No worktrees, no branches. Per `feedback_stay_on_main`.
- **No `Co-Authored-By` trailer** in commit messages. Per `feedback_no_co_authored_by`.
- **No git push or PR-opening suggestions.** Project is local-only. Per `feedback_no_remote_no_push_suggestions`.
- **Use `git -C <repo-root>`** for every git invocation. Per `feedback_git_use_dash_C_flag`.
- **Use `pnpm --dir`** if pnpm gets invoked (unlikely — this plan touches no JS dependencies). Per `feedback_pnpm_use_dir_flag`.
- **Never modify, skip, or weaken tests to make them pass.** No `[Fact(Skip=…)]`. Per `feedback_never_skip_tests_to_make_them_pass`. *(Largely irrelevant for this plan — no test files modified — but binding if a hook regression surfaces a pre-existing failure.)*
- **Stop-hook (`.claude/hooks/run-tests.sh`) does NOT block this plan's commits.** Reason: the plan writes only `.js`, `.md`, `.json` files — no tracked code (`.cs`, `.ts`, `.tsx`, `.csproj`, `.sln`). The `track-session-writes.js` hook (now also session-scoped per recent fix) won't record anything, so `run-tests.sh` exits 0 cleanly on every commit.
- **Each edit = one commit.** Per user direction 2026-05-17. Commit message format: `feat(playbook): <edit-N-short-description>` or `chore(playbook): ...` for config/memory edits.
- **`require-verify-against-codebase-before-spec.js`** does NOT gate this plan because plans write under `docs/superpowers/plans/`, not `specs/`. The plan file itself is exempt by the existing hook's path check.
- **Memory rule `feedback_brainstorm_spec_plan_execute_flow`** acknowledged and explicitly satisfied: the cohesion review at `.claude/skills-cohesion-review.md` IS the spec by structure (design + rationale + per-edit detail + resolved decisions). User confirmed 2026-05-17 that the review file functions as the spec; no separate doc under `docs/superpowers/specs/` is needed.

## Pre-flight research (BLOCKING — must complete before Edit 14)

- [ ] **Research Claude Code's pre-compaction event mechanism.** Edit 14 (the `state-rehydration` sidecar) needs to know whether Claude Code emits a `PreCompact` (or equivalent) hook event before context compaction. If yes, the snapshot can be triggered explicitly. If no, the per-turn opportunistic snapshotter is the only path. Authoritative sources: Anthropic's Claude Code hook documentation, the `superpowers:writing-skills` skill, `.claude/hooks/` example patterns. **Stop before Edit 14 until this is answered.** Document the finding inline in Edit 14's `SKILL.md`.

## File map

| File | Action | Edit # | Purpose |
|---|---|---|---|
| `.claude/skills/playbook/` | **create** | 1 | Skill directory scaffold |
| `.claude/skills/playbook/SKILL.md` | **create** | 2 | Skill body — when it fires, how it detects phase, how it reads the constitution |
| `.claude/skills/playbook/references/constitution.md` | **create** | 3 | Routing matrix — seven phases with chains, gating events, enforcement, consumes/produces |
| `.claude/skills/playbook/hooks/state-record-skill-fire.js` | **create** | 4 | PostToolUse hook on `Skill` tool — appends invoked-skill name to `.claude/state/playbook/<session_id>.json` |
| `.claude/skills/playbook/hooks/stage-start-detect.js` | **create** | 5 | UserPromptSubmit hook — fires on "let's start stage", "build feature", phase A advisory |
| `.claude/skills/playbook/hooks/pre-spec-write-gate.js` | **create** | 6 | PreToolUse hook (replaces `require-verify-against-codebase-before-spec.js`) — denies Write to `docs/superpowers/specs/` unless `superpowers:brainstorming` + `verify-against-codebase` fired this session |
| `.claude/skills/no-unjustified-deferrals/hooks/pre-write-deferral.js` | **modify** | 7 | Upgrade advisory → HARD: emit `decision: "block"` (not just `additionalContext`) when deferral phrases hit `docs/**` and the three-field template isn't satisfied |
| `.claude/skills/playbook/hooks/pre-stage-close-gate.js` | **create** | 8 | PreToolUse hook — denies Edit/Write that marks a stage `[x]` or `✅ Done` in a roadmap doc unless: (a) `sync-docs` + `changelog-sync` fired this session, (b) the stage's section has zero unchecked `[ ]` items (boundary = next `## ` per Q2-Option-A), (c) any newly-deferred items have matching `[ ]` lines under a receiving stage header in the same edit |
| `.claude/skills/playbook/hooks/pre-commit-gate.js` | **create** | 9 | PreToolUse hook on `Bash` matching `git commit` — denies the commit if the diff contains files matching `*.cs`, `*.ts`, `*.tsx`, `*.csproj`, `*.sln` AND `verify-against-codebase` hasn't fired since the most recent code Write. Per Q1-Option-C: pure `*.md` / `*.json` / `.gitignore` / `*.txt` diffs bypass the gate |
| `.claude/skills/playbook/hooks/pre-pr-review-detect.js` | **create — disabled** | 10 | UserPromptSubmit hook for `pre-PR-review` phase. **Not wired in `settings.json`** because Ceres has no remote (`feedback_no_remote_no_push_suggestions`). Lives on disk for future re-enablement |
| `.claude/settings.json` | **modify** | 11 | Wire the new hooks: 1 PostToolUse (state-record), 1 UserPromptSubmit (stage-start-detect), 4 PreToolUse (pre-spec-write-gate, pre-stage-close-gate, pre-commit-gate, plus the new build-warnings hook from Edit 15). The `require-verify-against-codebase-before-spec.js` entry gets replaced by `pre-spec-write-gate.js` |
| `.gitignore` | **modify** | 12 | Confirm `.claude/state/` is gitignored (it already is per existing entry — verify; add nothing if covered). Confirm the new state subdirs (`playbook/`, `state-rehydration/`, `surface-build-warnings/`) are covered by the existing pattern |
| `memory/MEMORY.md` + `memory/reference_playbook_skill.md` | **modify + create** | 13 | New index entry under a new section "Read this BEFORE invoking any chain of skills" plus the linked reference file |
| `.claude/skills/state-rehydration/` (full skill: `SKILL.md`, `references/snapshot-shape.md`, `hooks/snapshot-on-turn-end.js`, `hooks/detect-compaction.js`) | **create** | 14 | Sidecar skill for compaction survival. **Blocked on the pre-flight research above.** |
| `.claude/hooks/surface-build-warnings.js` + `.claude/skills/playbook/references/build-warnings.md` | **create** | 15 | PostToolUse hook on `Bash` matching build/test commands. Watches output for: Browserslist outdated, Tailwind "no utility classes", site.css size < 20 KB, stale `.claude/*.lock` files, .NET deprecation warnings, test runtime > 6 min. Per-session deduplication via `.claude/state/surface-build-warnings/<session_id>.json` |
| `.claude/commands/playbook.md` | **create** | bundled with Edit 2 | Slash command — invokes the `playbook` skill and prints the per-session chain status table |

## Commit-by-commit overview

| Commit | Subject | What lands | Blocking? |
|---|---|---|---|
| 1 | `chore(playbook): scaffold .claude/skills/playbook/ directory` | Empty directory tree: `playbook/`, `playbook/references/`, `playbook/hooks/` | No |
| 2 | `feat(playbook): SKILL.md + /playbook slash command` | `playbook/SKILL.md` (full body per cohesion review §4) + `.claude/commands/playbook.md` (slash command mirroring `/no-defer` shape) | No |
| 3 | `feat(playbook): constitution.md routing matrix` | `playbook/references/constitution.md` lifted verbatim from cohesion review §3 | No |
| 4 | `feat(playbook): state-record-skill-fire PostToolUse hook` | `playbook/hooks/state-record-skill-fire.js` + creates `.claude/state/playbook/` on first fire. Wired in `settings.json` separately at Edit 11 — this commit lands the file only | No |
| 5 | `feat(playbook): stage-start-detect UserPromptSubmit hook (advisory)` | `playbook/hooks/stage-start-detect.js`. Regex list for "let's start stage", "build feature X", "moving on to stage" | No |
| 6 | `feat(playbook): pre-spec-write-gate (replaces require-verify-against-codebase-before-spec.js)` | `playbook/hooks/pre-spec-write-gate.js`. Reads the state file to confirm `superpowers:brainstorming` + `verify-against-codebase` both fired. Emits `decision: "block"` if not. **Does NOT delete the old hook yet** — the old hook's `settings.json` entry remains until Edit 11 swaps it. Both hooks coexisting briefly is fine; the new one is a superset of the old | No |
| 7 | `feat(no-unjustified-deferrals): upgrade pre-write-deferral.js from advisory to HARD` | `.claude/skills/no-unjustified-deferrals/hooks/pre-write-deferral.js` modified: emits `decision: "block"` (in addition to or replacing the `additionalContext` block) when deferral phrases hit `docs/**` AND the well-formed-deferral guard (Stage X ref + `[ ]` checkbox + tooling/already-scheduled marker) doesn't satisfy. Cohesion review notes this single edit would have caught 5 of the 11 missed-fires by itself | No |
| 8 | `feat(playbook): pre-stage-close-gate PreToolUse hook` | `playbook/hooks/pre-stage-close-gate.js`. Detection logic: (a) the edit's `new_string` adds `✅ Done` OR changes a stage header from `[ ]` to `[x]`, (b) parses the stage section (start = the matching `## Stage N — ...` header in `new_string`; end = next `## ` per Q2-Option-A), (c) counts unchecked `[ ]` items in that range, (d) checks `state-record-skill-fire` for `sync-docs` + `changelog-sync` invocations this session. Blocks if any check fails | No |
| 9 | `feat(playbook): pre-commit-gate PreToolUse hook` | `playbook/hooks/pre-commit-gate.js`. Detection: matches Bash invocations of `git commit` (and variants like `git -C ... commit`, `git commit -m`, heredoc commits). Path filter per Q1-Option-C — runs `git diff --cached --name-only`, checks for `\.(cs\|ts\|tsx\|csproj\|sln)$`. If code files present AND `verify-against-codebase` hasn't fired since most recent code Write, block | No |
| 10 | `feat(playbook): pre-pr-review-detect hook (disabled, future use)` | `playbook/hooks/pre-pr-review-detect.js` lives on disk but NOT wired in `settings.json`. Comment at top explains: "Ceres is local-only; this hook is dormant. Re-wire if a remote is ever added" | No |
| 11 | `chore(playbook): wire new hooks into settings.json; remove the old require-verify-against-codebase-before-spec.js entry` | `.claude/settings.json` modified: 1 PostToolUse entry (state-record), 1 UserPromptSubmit (stage-start-detect), 4 PreToolUse (pre-spec-write-gate, pre-stage-close-gate, pre-commit-gate, AND the build-warnings hook from Edit 15 if Edit 15 lands before this commit — see ordering note). The existing `require-verify-against-codebase-before-spec.js` PreToolUse entry gets replaced (not duplicated) with the new `pre-spec-write-gate.js`. Validate via `python3 -c "import json; json.load(open('.claude/settings.json'))"`. **Ordering note:** wire Edit 15's hook here too only if Edit 15 lands before this commit; if Edit 15 lands after, this commit wires 5 hooks and Edit 15's commit re-edits this file to add the 6th. Recommend: land Edit 15 immediately after Edit 11 to keep settings.json edits in one place | No |
| 12 | `chore(playbook): verify .gitignore covers .claude/state/*` | Verify `.claude/state/` is gitignored (existing); confirm the playbook subdirs are covered. If the existing pattern already matches, this commit is empty-or-near-empty — that's fine, document it in the commit body. If gaps exist, add the specific subdir entries | No |
| 13 | `chore(memory): add playbook pointer to MEMORY.md + reference file` | `memory/MEMORY.md` gains a new section "Read this BEFORE invoking any chain of skills" with one index line. `memory/reference_playbook_skill.md` created with the full description mirroring the cohesion review's outline. Memory file is OUTSIDE the project tree (lives in `~/.claude/projects/...`) — handle from absolute path | No |
| 14 | `feat(state-rehydration): sidecar skill for compaction survival` | Full skill: `SKILL.md`, `references/snapshot-shape.md`, `hooks/snapshot-on-turn-end.js`, `hooks/detect-compaction.js`. State at `.claude/state/state-rehydration/<session_id>/snapshot.json`. Wired in `settings.json` in this same commit (one extra PostToolUse + one UserPromptSubmit). Pre-flight research finding documented in `SKILL.md` | **Yes — blocked on pre-flight research** |
| 15 | `feat(playbook): surface-build-warnings PostToolUse hook` | `.claude/hooks/surface-build-warnings.js` (top-level — watches `Bash`, not a skill hook) + `.claude/skills/playbook/references/build-warnings.md` (the corpus). State at `.claude/state/surface-build-warnings/<session_id>.json`. Wired in `settings.json` in this same commit. Warning corpus per cohesion review §5 Edit 15 table | No |

---

## Per-edit detail

For each edit below, the **Plan** section restates the cohesion-review intent for the executor's clarity, and the **Verification** section names the concrete check that proves the edit landed correctly.

### Edit 1 — Scaffold the `playbook` skill directory

**Plan:**
- [ ] Create `.claude/skills/playbook/`.
- [ ] Create `.claude/skills/playbook/references/`.
- [ ] Create `.claude/skills/playbook/hooks/`.

**Verification:**
- [ ] `ls .claude/skills/playbook/` shows `references/` and `hooks/` directories.
- [ ] Commit lands as a chore commit (no actual file content yet — `git diff --stat HEAD~1` shows only directory creation, which git tracks via `.gitkeep` or via the first file in subsequent commits. Recommend: skip `.gitkeep` files and let Edits 2/3/4 populate the dirs.)

### Edit 2 — Write `playbook/SKILL.md` and `/playbook` command

**Plan:**
- [ ] Write `playbook/SKILL.md` per cohesion review §4 (sections: trigger, phase-detection, state-file shape, chain-status output, slash-command behavior, linked memory).
- [ ] Write `.claude/commands/playbook.md` mirroring `/no-defer.md` shape — invokes the `playbook` skill and prints the per-session chain status table from §4.5.

**Verification:**
- [ ] `playbook/SKILL.md` has frontmatter (`name: playbook`, `description: ...`) and covers all 6 sections from cohesion review §4.
- [ ] `/playbook` appears in the skill registry on next session start (test in a follow-up session — flagged here as a manual verification step).

### Edit 3 — Write `playbook/references/constitution.md`

**Plan:**
- [ ] Lift cohesion review §3 verbatim into `playbook/references/constitution.md`. All seven phases with their resolved enforcement annotations: stage-start (advisory), pre-spec-write (HARD), mid-build (advisory), pre-deferral (HARD), pre-stage-close (HARD), pre-commit (HARD), pre-PR-review (disabled-in-Ceres).
- [ ] Confirm both decisions from §6 land in the constitution: Phase E uses Option A (next `## ` heading); Phase F uses Option C (path-based bypass).
- [ ] Constitution closes with the disabled-skills list (`superpowers:using-git-worktrees`, `superpowers:finishing-a-development-branch`, `superpowers:requesting-code-review`, `superpowers:receiving-code-review`, `superpowers:dispatching-parallel-agents` per Ceres single-machine local-only constraints).
- [ ] One paragraph acknowledging `frontend-orchestrator` as a sibling routing skill that owns the frontend domain pipeline; `playbook` covers cross-cutting cohesion only.

**Verification:**
- [ ] `grep "Phase E\|Phase F\|Option A\|Option C" playbook/references/constitution.md` returns the expected hits.
- [ ] All seven phases have the four required fields: chain, gating event, enforcement, consumes/produces.

### Edit 4 — `state-record-skill-fire.js` PostToolUse hook

**Plan:**
- [ ] Write `playbook/hooks/state-record-skill-fire.js`.
- [ ] Trigger: PostToolUse on `Skill` tool only.
- [ ] State file: `.claude/state/playbook/<session_id>.json` with shape `{ "skills_fired": [{ "name": "...", "ts": "ISO-8601" }, ...], "writes_since_last_skill": [...] }`. The `writes_since_last_skill` array tracks file paths written between skill invocations — used by Phase F's "verify-against-codebase fired since most recent code Write" check.
- [ ] Uses `process.env.CLAUDE_PROJECT_DIR || process.cwd()` for project root (same model as existing hooks).
- [ ] Empty / corrupted state file is safely handled (initialize to `{ "skills_fired": [], "writes_since_last_skill": [] }`).

**Verification:**
- [ ] Smoke test (same shape as the no-unjustified-deferrals test pattern): pipe a fake `Skill`-tool PostToolUse payload through `node` and confirm the state file appears with the expected shape.
- [ ] Hook is NOT yet wired in settings.json (Edit 11 wires it).

### Edit 5 — `stage-start-detect.js` UserPromptSubmit hook (advisory)

**Plan:**
- [ ] Write `playbook/hooks/stage-start-detect.js`.
- [ ] Regex list (initial set, expand if walked sessions reveal misses): `/\blet'?s (start|move on (to|with)|begin) stage \w+/i`, `/\bbuild (the )?feature \w+/i`, `/\bstage \d+(\.\d+)?\s*[:—-]?\s*(start|begin)/i`, `/\bmoving on to stage \w+/i`, `/\bkick off stage/i`.
- [ ] Output: `additionalContext` reminding the agent to invoke `superpowers:brainstorming` as the next step.
- [ ] No blocking — advisory only.

**Verification:**
- [ ] Smoke test: prompt "let's start stage 10" → hook fires with the brainstorming reminder.
- [ ] Smoke test: prompt "read the README" → hook silent (exit 0).

### Edit 6 — `pre-spec-write-gate.js` PreToolUse hook (replaces existing spec gate)

**Plan:**
- [ ] Write `playbook/hooks/pre-spec-write-gate.js`.
- [ ] Trigger: PreToolUse on `Write|Edit|MultiEdit` where the `file_path` is under `docs/superpowers/specs/`.
- [ ] Reads `.claude/state/playbook/<session_id>.json`. If `superpowers:brainstorming` has NOT fired AND `verify-against-codebase` has NOT fired this session, emit `decision: "block"` with stderr explaining which skill is missing.
- [ ] Existing-spec-rewrite exemption: if `file_path` already exists on disk, allow the write (mirrors the existing hook's `path-already-exists` carve-out).
- [ ] The OLD `require-verify-against-codebase-before-spec.js` is NOT deleted in this commit — Edit 11 swaps the `settings.json` entry. The two hooks coexisting briefly is harmless; the new is a superset of the old.

**Verification:**
- [ ] Smoke test: PreToolUse payload for Write to `docs/superpowers/specs/2026-05-17-test.md` with empty state → block.
- [ ] Smoke test: same payload, after seeding state file with `verify-against-codebase` fired → allow.
- [ ] Smoke test: Write to an existing spec file → allow (rewrite exemption).

### Edit 7 — Upgrade `pre-write-deferral.js` from advisory to HARD

**Plan:**
- [ ] Modify `.claude/skills/no-unjustified-deferrals/hooks/pre-write-deferral.js`.
- [ ] Current behavior: emits `additionalContext` reminding to invoke `no-unjustified-deferrals` skill.
- [ ] New behavior: ALSO emits `decision: "block"` when the well-formed-deferral guard (Stage X ref + `[ ]` checkbox + tooling/already-scheduled marker) does NOT satisfy. The advisory text stays.
- [ ] Allowed-pattern guard remains — well-formed deferrals (Stage ref + checkbox + reason marker) still pass silently.

**Verification:**
- [ ] Smoke test: a docs/ Write containing "doesn't change structural" without the three guard fields → block.
- [ ] Smoke test: a docs/ Write containing "deferred to Stage 9.2" with `- [ ]` line and "tooling gap" marker → silent (allowed pattern).
- [ ] Smoke test: a non-docs Write containing the phrase → silent (path filter).
- [ ] Confirm the existing `references/deferral-phrases.md` corpus is unchanged.

### Edit 8 — `pre-stage-close-gate.js` PreToolUse hook

**Plan:**
- [ ] Write `playbook/hooks/pre-stage-close-gate.js`.
- [ ] Trigger: PreToolUse on `Edit|Write|MultiEdit` where `file_path` matches `docs/roadmap-phase-*.md`.
- [ ] Detection: the `new_string` contains `✅ Done` added to a stage header, OR a `## Stage N — ... ` header switching to `[x]` from `[ ]`.
- [ ] Stage-section parse: start = the matching `## Stage N — ...` header in `new_string`; end = next `## ` heading at level-2 (per Q2-Option-A); body = lines between. Count `^\s*-\s+\[ \]\s+` matches in the body.
- [ ] Move-to-receiving-stage allowance: if a `[ ]` line in the closing stage's body is paired with an identical (or "see deferral entry in"-cross-referenced) `[ ]` line in a different stage's section in the same `new_string`, it doesn't count as "unchecked in the closing stage".
- [ ] Skill checks: read `.claude/state/playbook/<session_id>.json` — confirm `sync-docs` AND `changelog-sync` both invoked this session.
- [ ] Block conditions: (a) any unchecked `[ ]` items remain in the closing stage's body AND they aren't paired in a receiving stage, OR (b) `sync-docs` not invoked, OR (c) `changelog-sync` not invoked.

**Verification:**
- [ ] Smoke test: Edit marking a stage Done with 1 unchecked `[ ]` remaining → block.
- [ ] Smoke test: same Edit but the `[ ]` line is paired with `- [ ]` under a "Stage 9" header in the same `new_string` → allow.
- [ ] Smoke test: stage Done with no unchecked items but `sync-docs` not invoked → block with "sync-docs missing" message.
- [ ] Smoke test: stage Done, all conditions met → silent.

### Edit 9 — `pre-commit-gate.js` PreToolUse hook

**Plan:**
- [ ] Write `playbook/hooks/pre-commit-gate.js`.
- [ ] Trigger: PreToolUse on `Bash` matching `git commit` (regex covers `git commit`, `git -C ... commit`, with `-m`, with heredoc).
- [ ] Path filter (Q1-Option-C): `git -C $project diff --cached --name-only` — check for files matching `\.(cs|ts|tsx|csproj|sln)$`.
- [ ] If NO matching files in the staged diff → allow (the commit is doc/config only).
- [ ] If matching files present: read state file. If `verify-against-codebase` has fired since the most recent entry in `writes_since_last_skill` for a tracked-code file → allow. Otherwise → block.

**Verification:**
- [ ] Smoke test: `git commit` payload, staged diff is `*.md` only → allow.
- [ ] Smoke test: same, staged diff contains a `*.cs` file, state file shows no `verify-against-codebase` since last code Write → block with stderr naming the unverified files.
- [ ] Smoke test: same as above but state file shows `verify-against-codebase` fired after the most recent code Write → allow.

### Edit 10 — `pre-pr-review-detect.js` (disabled in Ceres)

**Plan:**
- [ ] Write `playbook/hooks/pre-pr-review-detect.js` as a placeholder.
- [ ] Header comment: "Ceres is local-only (`feedback_no_remote_no_push_suggestions`); this hook is NOT wired in settings.json. Re-enable by adding to UserPromptSubmit hooks if a remote is ever added."
- [ ] Logic body left as a no-op `process.exit(0)` — the structure is there for future use; no behavior yet.

**Verification:**
- [ ] File exists at the expected path.
- [ ] `grep "pre-pr-review-detect" .claude/settings.json` returns nothing (not wired).

### Edit 11 — Wire hooks into `settings.json`

**Plan:**
- [ ] Read current `.claude/settings.json` (already verified valid JSON).
- [ ] Add to `PostToolUse`: 1 entry for `state-record-skill-fire.js` on `Skill` matcher. **Recommended:** also add the build-warnings entry here if Edit 15 has already landed; otherwise leave for Edit 15's commit.
- [ ] Add to `UserPromptSubmit`: 1 entry for `stage-start-detect.js` (empty matcher).
- [ ] Add to `PreToolUse`: 3 entries — `pre-spec-write-gate.js` (matcher `Write|Edit|MultiEdit`), `pre-stage-close-gate.js` (matcher `Edit|Write|MultiEdit`), `pre-commit-gate.js` (matcher `Bash`).
- [ ] **Remove** the existing `require-verify-against-codebase-before-spec.js` entry from `PreToolUse` (its job is taken over by `pre-spec-write-gate.js`).
- [ ] Validate: `python3 -c "import json; json.load(open('.claude/settings.json'))"` exits 0.

**Verification:**
- [ ] `grep -c "playbook/hooks" .claude/settings.json` returns 4 (or 5 if Edit 15's hook is wired here too).
- [ ] `grep -c "require-verify-against-codebase-before-spec.js" .claude/settings.json` returns 0.
- [ ] Manual: open settings.json and confirm the JSON structure is clean (no trailing commas, no duplicates).

### Edit 12 — Verify `.gitignore` covers state subdirs

**Plan:**
- [ ] Read `.claude/.gitignore` (project root).
- [ ] Confirm `.claude/state/` is gitignored. If yes, this commit is informational only — the commit body documents the verification result.
- [ ] If the existing pattern doesn't cover all needed subdirs (`playbook/`, `state-rehydration/`, `surface-build-warnings/`), add them.
- [ ] If the commit ends up empty (nothing to change), DO NOT create an empty commit — skip this edit's commit and document in the next commit's body that .gitignore was verified.

**Verification:**
- [ ] `git check-ignore .claude/state/playbook/test.json` returns success (the file would be ignored if it existed).
- [ ] Same for `.claude/state/state-rehydration/test.json` and `.claude/state/surface-build-warnings/test.json`.

### Edit 13 — Memory pointer

**Plan:**
- [ ] Read `~/.claude/projects/<project-slug>/memory/MEMORY.md`.
- [ ] Add a new section heading "## Read this BEFORE invoking any chain of skills" placed before the "Read this BEFORE responding to ANY circling signal" section (so it appears alphabetically/structurally near the other skill-pointer sections).
- [ ] Add the index entry per cohesion review §5 Edit 13.
- [ ] Create `memory/reference_playbook_skill.md` with the full description: when it fires, the seven phases with enforcement, state-file location, slash-command behavior, build rationale (2026-05-17 cohesion audit), linked memories.
- [ ] **DO NOT** touch any other memory file in this commit.

**Verification:**
- [ ] `grep -c "playbook" ~/.claude/projects/<project-slug>/memory/MEMORY.md` returns at least 1.
- [ ] The reference file exists with frontmatter `name: reference-playbook-skill`, `type: reference`.

### Edit 14 — `state-rehydration` sidecar skill

**BLOCKED on pre-flight research.** Do NOT start this edit until the Pre-flight Research step at the top is complete.

**Plan:**
- [ ] Document the research finding in `state-rehydration/SKILL.md`: does Claude Code emit a pre-compaction event? If yes, name it and cite source. If no, document the opportunistic-snapshot fallback.
- [ ] Write `state-rehydration/SKILL.md`: trigger description, mechanism (per-turn snapshot + compaction-detection), state file shape (`.claude/state/state-rehydration/<session_id>/snapshot.json`), what's snapshotted (skills fired, AskUserQuestion sessions, last 5 confirmed decisions, last 5 files written, current open spec/plan/roadmap).
- [ ] Write `references/snapshot-shape.md`: the exact JSON schema.
- [ ] Write `hooks/snapshot-on-turn-end.js`: PostToolUse on any tool (or PreToolUse on UserPromptSubmit if research says that's the right boundary). Reads `playbook` state and the conversation context (whatever the hook has access to) and writes the snapshot.
- [ ] Write `hooks/detect-compaction.js`: UserPromptSubmit hook. Checks for compaction evidence (concrete check TBD based on research findings); if detected, emits `additionalContext` directing the agent to re-read the snapshot.
- [ ] Wire both hooks into settings.json in this same commit.

**Verification:**
- [ ] Smoke test: snapshot hook fires on a fake PostToolUse payload, snapshot file appears with expected JSON shape.
- [ ] Smoke test: detect-compaction hook fires on a payload where snapshot disagrees with visible context, emits the rehydration reminder.

### Edit 15 — `surface-build-warnings.js` PostToolUse hook

**Plan:**
- [ ] Write `.claude/hooks/surface-build-warnings.js` (top-level — watches Bash, not a skill hook).
- [ ] Trigger: PostToolUse on `Bash` matching commands containing `dotnet build`, `dotnet test`, `pnpm build`, `pnpm test`, `pnpm --dir ProjectCeres.Client`.
- [ ] Parse the tool_response output through the warning corpus (lifted from cohesion review §5 Edit 15 table): Browserslist outdated, Tailwind "no utility classes", site.css size < 20 KB, stale `.claude/*.lock` files (mtime > 1h), .NET deprecation warnings (`NETSDK10XX`, obsolete API warnings), test runtime > 6 min (per `project_test_suite_performance` memory).
- [ ] Per-session deduplication via `.claude/state/surface-build-warnings/<session_id>.json` — record warning IDs already surfaced this session; don't re-surface.
- [ ] Output: `additionalContext` (advisory, not blocking) with the warning text + remediation hint.
- [ ] Write `.claude/skills/playbook/references/build-warnings.md` — the canonical corpus with patterns, sources, remediation actions. Hook reads from here so the corpus is greppable and editable independently of the hook code.
- [ ] Wire in `settings.json` in this same commit.

**Verification:**
- [ ] Smoke test: PostToolUse payload with `tool_input.command = "dotnet build"` and `tool_response` containing "Browserslist: caniuse-lite is outdated" → hook fires with the Browserslist remediation message.
- [ ] Smoke test: same payload, second invocation in the same session → silent (deduplication).
- [ ] Smoke test: PostToolUse payload with a `dotnet test` runtime header showing 7m 12s → fires with the "test runtime regression" message.
- [ ] Smoke test: PostToolUse payload with `ls` command (not a build/test invocation) → silent (matcher filter).

---

## Post-build verification

After all 15 commits land, the executor performs an end-to-end smoke test:

- [ ] **All 15 commits present in `git log --oneline`** with the expected subject prefixes.
- [ ] **`.claude/settings.json` valid JSON** and contains entries for: state-record-skill-fire, stage-start-detect, pre-spec-write-gate, pre-stage-close-gate, pre-commit-gate, snapshot-on-turn-end, detect-compaction, surface-build-warnings. Does NOT contain `require-verify-against-codebase-before-spec.js`.
- [ ] **`/playbook` slash command available** in the skill registry (verified by starting a new session — likely a manual step at the end).
- [ ] **State directory tree exists** at `.claude/state/` with subdirs for `playbook/`, `state-rehydration/`, `surface-build-warnings/` (gitignored).
- [ ] **The cohesion review file at `.claude/skills-cohesion-review.md` is referenced by `reference_playbook_skill.md` memory entry** — so future sessions can find the design context.
- [ ] **Memory `MEMORY.md` index has the new section "Read this BEFORE invoking any chain of skills"** with the playbook pointer.

## Rollback procedure

If a commit breaks the hook system mid-build, the executor:
1. Confirms which hook is failing via `cat ~/.claude/projects/<project-slug>/<session_id>/hook-errors.log` (or wherever hook stderr lands — research before-hand if uncertain).
2. Reverts the specific failing commit with `git -C <repo-root> revert <commit-sha> --no-edit`.
3. Documents the issue in the next commit's body.
4. Pre-existing memory rule `feedback_confirm_before_destructive_git` requires explicit user confirmation before any non-revert destructive git action (`reset --hard`, force-push, `branch -D`). A revert is not destructive (creates a new commit) but the executor should still flag it to the user before running.

## What this plan does NOT cover

- **Migration of any existing skill's content.** No SKILL.md of `deep-fix-mode`, `decision-mode`, `no-unjustified-deferrals`, `verify-against-codebase`, `sync-docs`, `changelog-sync`, or `dev-teacher` is modified by this plan. The constitution embedded matrix means existing skills don't need a `consumes`/`produces` frontmatter — the routing logic lives entirely in `playbook/references/constitution.md`.
- **The `frontend-orchestrator` skill.** Acknowledged in the constitution as a sibling routing skill (covers frontend domain pipeline); `playbook` covers cross-cutting cohesion only. No coordination logic between the two in v1.
- **Performance tuning of hook execution time.** Hooks must be reasonably fast (each one runs on every gated tool call), but no explicit benchmark gate is set. If a hook adds >100ms perceived latency, flag for follow-up tuning — don't fix mid-build.
- **Documentation of the playbook system in `docs/`.** The skill lives entirely under `.claude/`. Project docs (`docs/architecture.md`, etc.) are not touched. The `dev-teacher` skill could later route a guide entry to `docs/guide/` if the user requests it — out of scope here.
