# playbook constitution

Single source of truth for the chain routing. The `playbook` skill reads this file. Hooks reference these phases by name. Do NOT duplicate this content into per-skill frontmatter — the user explicitly chose embedded over distributed (session `322c64dc`, lines 772–812).

## Phase legend

- **HARD** — gate denies the triggering tool call (`decision: "deny"` / `permissionDecision: "deny"`) until the required skill has fired this session.
- **ADVISORY** — gate emits `additionalContext` via UserPromptSubmit but does not block.

State for all gates: `.claude/state/playbook/<session_id>.json` — recorded by `playbook/hooks/state-record-skill-fire.js`.

---

## The seven phases

### Phase A — `stage-start` (advisory)

**Gating event.** User prompt contains: "let's move on with stage X", "what's next", "proceed with stage", "let's build feature", "implement Y from the roadmap", "kick off stage", "moving on to stage".

**Required chain.**

1. `superpowers:using-superpowers` (auto-fires by superpowers convention)
2. `superpowers:brainstorming` — produces design + user approval

**Enforcement.** Advisory via `playbook/hooks/stage-start-detect.js` (UserPromptSubmit). Hook injects: "stage-start detected — invoke `superpowers:brainstorming` first; do NOT jump to plan-mode plan."

**What counts as fired.** A `Skill` tool call with `skill: "superpowers:brainstorming"` recorded in `.claude/state/playbook/<session_id>.json`.

**Consumes / produces.**
- Consumes: user request to start a stage or build a feature.
- Produces: an approved design + a spec under `docs/superpowers/specs/` (feeds Phase B's gate).

**Per-link hand-off.** Per `feedback_brainstorm_spec_plan_execute_flow`: "brainstorm → spec → plan → execute, with review at every boundary."

---

### Phase B — `pre-spec-write` (HARD)

**Gating event.** PreToolUse on Write/Edit/MultiEdit where `file_path` matches `docs/superpowers/specs/*.md` AND the file does not already exist on disk.

**Required chain.** Both of the following must be in the session state file:

1. `superpowers:brainstorming` (established the design)
2. `verify-against-codebase` (audited the design against project conventions)

**Enforcement.** HARD via `playbook/hooks/pre-spec-write-gate.js`. **Replaces** the existing `require-verify-against-codebase-before-spec.js` (whose scope is a subset of this gate). Bypass: the file already exists (rewrite of an existing spec).

**What counts as fired.** A `Skill` tool call with the required skill name recorded in state, OR an explicit `verify-against-codebase` mention in the assistant transcript (preserves the bypass behavior of today's hook).

**Why HARD not ADVISORY.** The originating motivation for the existing hook was the Stage 6b.1 incident where Claude proposed a homegrown `MfaTicketService` that duplicated framework features. That class of error is high-cost — caught pre-dispatch it's a re-plan; caught mid-execution it's a subagent round trip plus rework.

**Consumes / produces.**
- Consumes: approved design from Phase A + spec file path under `docs/superpowers/specs/`.
- Produces: the new spec file (the Write completes).

---

### Phase C — `mid-build` (advisory, plus existing live guards)

**Gating event.** Continuous — fires throughout the build phase via the existing hooks. No additional `playbook` hook needed here; this phase is the existing trio:

1. `deep-fix-mode` — auto-fires on circling signals via `frustration-detect.js`, `loop-fingerprint.js`, `same-target-edit-count.js`.
2. `decision-mode` — auto-fires on decision-asking prompts via `decision-detect.js`.
3. `no-unjustified-deferrals` — auto-fires on deferral language and pushback via `pre-write-deferral.js`, `pushback-detect.js`.

**Enforcement.** All ADVISORY today. **Phase D (`pre-deferral`) escalates one of them to HARD.**

**Consumes / produces.**
- Consumes: in-progress build state (edits, tool calls, user prompts).
- Produces: advisory `additionalContext` reminders; in Phase D, a hard-block.

---

### Phase D — `pre-deferral` (HARD)

**Gating event.** PreToolUse Edit/Write/MultiEdit where the new content matches deferral phrases (per the existing regex list in `no-unjustified-deferrals/hooks/pre-write-deferral.js`) AND the well-formed-deferral guard (Stage X ref + `[ ]` checkbox + tooling/already-scheduled marker) does NOT satisfy.

**Required chain.**

1. `no-unjustified-deferrals` must have fired this session OR the write must include all three required fields.

**Enforcement.** HARD via an upgraded version of `pre-write-deferral.js` that emits `permissionDecision: "deny"` when the gate fails. **This is the single most important change in the cohesion-review proposal** — incident 2.3#5 (verbatim user quote: "Fixing an issue is never out of scope") is the exact scenario this prevents.

**What counts as fired.** Same state-file check as Phase B.

**Consumes / produces.**
- Consumes: a docs/ Write containing deferral language.
- Produces: either a passing deferral entry (with three fields) OR a hard-block denying the Write.

**Hand-off.** A passing deferral entry hands to `sync-docs` (so the receiving-stage `[ ]` and the planning-doc entry both land per `feedback_defer_work_to_all_three_docs`).

---

### Phase E — `pre-stage-close` (HARD)

**Gating event.** PreToolUse Edit/Write/MultiEdit where the edit changes `- [ ]` to `- [x]` for a stage header in a roadmap doc (`docs/roadmap-phase-*.md`), OR adds `✅ Done` near a stage header.

**Required chain.**

1. `sync-docs` must have fired this session.
2. `changelog-sync` must have fired this session.
3. The stage's section in the diff has **zero remaining `[ ]` lines** — verified by parsing the post-edit content. Boundary = next `## ` heading at level-2 depth (**Option A**, resolved 2026-05-17). Any unchecked item must either be ticked OR moved to the receiving stage's checklist with a back-reference (per `feedback_deferral_requires_receiving_stage_checkbox`).
4. No open `no-unjustified-deferrals` deferrals in this session lack their tripwire field.

**Enforcement.** HARD via `playbook/hooks/pre-stage-close-gate.js`. Direct motivation: incident 2.2#8 (verbatim user quote: "you left some checkboxes in Stage 7 without checking them and marked the stage as finished").

**What counts as fired.** State-file check for skills + parse of the stage section in the proposed post-edit content. Stage section start = the matching `## Stage N — ...` header in `new_string`; end = next `## ` heading.

**Consumes / produces.**
- Consumes: a roadmap edit marking a stage Done.
- Produces: either the edit completes OR a hard-block with the named missing requirement(s).

---

### Phase F — `pre-commit` (HARD, conditional)

**Gating event.** PreToolUse Bash where the command matches `^\s*git\s+(-C\s+\S+\s+)?commit\b` AND the staged diff contains files matching `\.(cs|ts|tsx|csproj|sln)$`.

**Required chain.**

1. `verify-against-codebase` must have fired this session against the latest diff.

**Enforcement.** HARD via `playbook/hooks/pre-commit-gate.js`. **Path-based bypass (Option C, resolved 2026-05-17):** pure documentation / config / gitignore diffs (`*.md`, `*.json`, `.gitignore`, `*.txt`) skip the gate. Rationale: `verify-against-codebase` exists to catch code-convention conflicts — it has nothing meaningful to say about doc-only commits.

**What counts as fired.** State-file check for `verify-against-codebase` invocation AFTER the most recent code Write in this session. The gate inspects: was the skill run AFTER the most recent code Write? If yes, allow. If the last code Write was after the last verify-fire, deny with "verify-against-codebase needs to re-run against the latest diff".

**Consumes / produces.**
- Consumes: a `git commit` Bash invocation + the current staged diff.
- Produces: either the commit proceeds OR a hard-block naming the unverified files.

---

### Phase G — `pre-PR-review` (advisory, disabled by default in Ceres)

**Gating event.** User prompt contains: "open a PR", "request review", "ready for review".

**Required chain.**

1. `superpowers:requesting-code-review`

**Enforcement.** Advisory via `playbook/hooks/pre-pr-review-detect.js` (UserPromptSubmit). **Per `feedback_no_remote_no_push_suggestions`, Project Ceres has no remote and never opens PRs.** The hook is shipped disabled by default — its detection logic flags the phrasing in case the user starts an external PR-driven workflow later, but the hook is NOT wired in `settings.json`. Re-enable by adding the hook entry to `UserPromptSubmit` if a remote is ever added.

**Consumes / produces.**
- Consumes: user prompt mentioning PR/review.
- Produces (when enabled): advisory `additionalContext` reminding to run `superpowers:requesting-code-review`.

---

## State tracking

**File.** `.claude/state/playbook/<session_id>.json`.

**Shape.**

```json
{
  "fired": ["superpowers:brainstorming", "verify-against-codebase", "no-unjustified-deferrals"],
  "fired_with_timestamps": [
    {"skill": "superpowers:brainstorming", "at": "2026-05-17T03:12:01Z", "tool_use_index": 14},
    {"skill": "verify-against-codebase", "at": "2026-05-17T03:18:33Z", "tool_use_index": 27}
  ],
  "writes_since_last_skill": [
    {"file": "ProjectCeres/Common/Authentication/EmailChangeService.cs", "at": "2026-05-17T03:25:01Z"}
  ],
  "open_deferrals": [
    {"file": "docs/roadmap-phase-three.md", "stage": "Stage 6.15", "added_at_tool_use_index": 39, "tripwire": "FIXME"}
  ]
}
```

**Lifecycle.** Initialised empty at first hook write. Persists across `/compact`. A new session = a new file (the session_id changes).

**Writers.**

- `playbook/hooks/state-record-skill-fire.js` (PostToolUse on `Skill` tool) — appends to `fired` and `fired_with_timestamps`.
- The same hook also tracks `writes_since_last_skill` so Phase F can answer "did `verify-against-codebase` fire since the most recent code Write?"
- `no-unjustified-deferrals/hooks/pre-write-deferral.js` upgrade — appends to `open_deferrals` when a deferral entry is written.

**Readers.** Every HARD gate (Phases B, D, E, F).

---

## What counts as fired

Default rule: a `Skill` tool call with the exact `skill` argument value, recorded in `fired`.

**Bypass-friendly carve-out** (preserves existing hook behavior): for `verify-against-codebase`, an explicit literal string `verify-against-codebase` in the latest assistant message also counts. Rationale: the existing spec-write hook scans the transcript for the string; behavior preservation is non-negotiable to avoid surprise denials.

---

## Disabled-in-Ceres skills

Two superpowers skills are project-disabled per memory:

- `superpowers:finishing-a-development-branch` — local-only, no branches per `feedback_stay_on_main`.
- `superpowers:using-git-worktrees` — same memory.

Three more are situationally inappropriate for Ceres' single-machine local-only workflow:

- `superpowers:requesting-code-review` — no remote, no PRs (Phase G is disabled).
- `superpowers:receiving-code-review` — same reason.
- `superpowers:dispatching-parallel-agents` — used selectively when the user explicitly invokes it; not part of the default chain.

`playbook` does NOT route to the first two. The latter three remain available via direct invocation when the user opts in.

---

## Sibling routing skill — `frontend-orchestrator`

`frontend-orchestrator` owns the frontend domain pipeline (discovery → build → refine → simplify → harden → system maintenance for `ProjectCeres.Client/`). `playbook` covers **cross-cutting cohesion only** — the seven phases above. The two skills are siblings, not nested: when a frontend phase boundary is crossed (e.g. "build the X page"), `frontend-orchestrator` routes the frontend pipeline; when a cross-cutting phase boundary is crossed (e.g. "let's close Stage 7"), `playbook` enforces the constitution.

No coordination logic between the two skills in v1. If they conflict in practice, the user surfaces it and a follow-up edit reconciles them.

---

## Slash-command escape hatch

The constitution does not block slash commands. `/deep-fix`, `/plain`, `/no-defer`, `/verify`, `/playbook` continue to work as today. A future thought: `/playbook` could optionally accept `--bypass <phase>` to record an intentional bypass; not implemented in v1.

---

## Decisions resolved 2026-05-17

The cohesion review at `.claude/skills-cohesion-review.md` §6 lists three open questions that were all answered before the build started:

1. **Phase F bypass** — **Option C, path-based**. Only Bash `git commit` invocations whose staged diff contains `\.(cs|ts|tsx|csproj|sln)$` files trigger the gate. Doc-only commits bypass entirely.
2. **Phase E stage-section boundary** — **Option A, markdown convention**. The next `## ` heading at level-2 depth ends the closing stage's section. Project Ceres roadmaps don't use mid-section `## ` headings; markdown's default reading model is the right default.
3. **`/playbook` slash command** — **yes, add it**. Mirrors `/deep-fix`, `/plain`, `/no-defer`, `/verify` symmetry.
