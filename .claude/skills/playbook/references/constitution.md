# playbook constitution

Single source of truth for the chain routing. The `playbook` skill reads this file. Hooks reference these phases by name. Do NOT duplicate this content into per-skill frontmatter — the user explicitly chose embedded over distributed (session `322c64dc`, lines 772–812).

## Phase legend

- **HARD** — gate denies the triggering tool call (`decision: "deny"` / `permissionDecision: "deny"`) until the required skill has fired this session.
- **ADVISORY** — gate emits `additionalContext` via UserPromptSubmit but does not block.

State for all gates: `.claude/state/playbook/<session_id>.json` — recorded by `playbook/hooks/state-record-skill-fire.js`.

---

## The seven phases (plus Phase A′ and Phase A″)

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

### Phase A′ — `frontend-touch` (advisory)

**Gating event.** Either of:

1. **Prompt-side.** UserPromptSubmit where the prompt contains frontend phrasing: "build the X page/component/drawer", "design this", "redesign", "polish", "review the UI", "audit accessibility", "make this bolder/quieter/tighter", "fix the empty state", "design system", "design tokens", or any direct mention of `ProjectCeres.Client/`, a `.tsx`/`.ts` filename, or `shadcn`/`tailwind`/`vite`/`vitest`. Full regex list in `playbook/hooks/frontend-touch-detect.js`.
2. **Spec-content-side.** PostToolUse Edit/Write/MultiEdit on `docs/superpowers/specs/*.md` where the new content contains frontend signals (`ProjectCeres.Client`, `.tsx`, shadcn/Tailwind names, UI nouns, React hook names, `docs/design-system.md`). Full regex list in `playbook/hooks/frontend-spec-postcheck.js`.

**Required chain.**

1. `frontend-orchestrator` — routes between `frontend-design`, `vercel-react-best-practices`, `web-design-guidelines`, `impeccable`, and `docs/design-system.md` based on phase (discovery → build → refine → simplify → harden → system maintenance).

**Enforcement.** ADVISORY via two hooks:

- `playbook/hooks/frontend-touch-detect.js` (UserPromptSubmit) — emits an advisory before brainstorming begins.
- `playbook/hooks/frontend-spec-postcheck.js` (PostToolUse on Edit/Write/MultiEdit) — emits an advisory if a spec write contained frontend signals but `frontend-orchestrator` never fired.

Both hooks no-op when `frontend-orchestrator` has already fired this session (state-file check).

**What counts as fired.** A `Skill` tool call with `skill: "frontend-orchestrator"` recorded in `.claude/state/playbook/<session_id>.json`.

**Consumes / produces.**
- Consumes: a user prompt with frontend phrasing OR a spec write with frontend signals.
- Produces: advisory `additionalContext` reminding to invoke `frontend-orchestrator`. The orchestrator's own pipeline then runs.

**Why ADVISORY not HARD.** Frontend orchestrator selection is judgment-driven (which phase, which sub-skill); a hard-block on UI vocabulary would false-positive on backend specs that merely reference a frontend file. Start advisory, escalate later if real misses accumulate.

**Hand-off.** Phase A′ runs in parallel with Phase A (`stage-start`) — both can fire on the same prompt, both are advisory. When both fire: `superpowers:brainstorming` and `frontend-orchestrator` both come before any Write.

---

### Phase A″ — `brainstorming-needs-decision-mode` (advisory)

**Gating event.** PostToolUse on Skill where `tool_input.skill === "superpowers:brainstorming"` AND `decision-mode` is not yet in this session's `fired` list.

**Required chain.**

1. `decision-mode` style must apply to every proposal, option, and trade-off emitted during the brainstorm — three-layer Container → Term → Why-here, no forbidden words inside Layer 2 unless defined in the same paragraph.

**Enforcement.** ADVISORY via `playbook/hooks/brainstorm-needs-decision-mode.js`. The hook does not require `decision-mode` to be invoked as a `Skill` call; it requires the *style* to apply. Suppressed when `decision-mode` has already fired this session (because then its rules are already in scope).

**Why ADVISORY not HARD.** `superpowers:brainstorming` is a long interactive flow with many tool calls; hard-blocking each one would be disruptive. The advisory is sufficient because the agent reads it on every Skill fire and the user can correct in real time. If real misses accumulate, escalate to HARD with a PreToolUse gate on the brainstorming Skill invocation.

**What counts as resolved.** Either (a) the user observes plain-language style and does not push back, OR (b) `decision-mode` is invoked as a Skill (which suppresses the advisory).

**Consumes / produces.**
- Consumes: a `superpowers:brainstorming` Skill invocation.
- Produces: advisory `additionalContext` reminding to apply `decision-mode` style to proposals.

**Hand-off.** Runs in parallel with Phase A (`stage-start`) and Phase A′ (`frontend-touch`); all three can fire on the same turn, all are advisory. When all three fire: brainstorm with plain-language proposals, route the frontend pipeline via `frontend-orchestrator` if applicable.

**Origin.** User pushback 2026-05-17 after the stage-start advisory routed to `superpowers:brainstorming` but the resulting proposals still leaked framework jargon ("middleware", "lifecycle", "DbContext"). The brainstorming skill itself is silent on language style — it relies on the host project's other skills to enforce that. Without a coordination hook, the two skills are siblings that don't know about each other.

---

### Phase B — `pre-spec-write` (HARD, tiered)

**Gating event.** PreToolUse on Write/Edit/MultiEdit where `file_path` matches `docs/superpowers/specs/*.md` AND the file does not already exist on disk.

**Required chain.** Both of the following must be in the session state file:

1. `superpowers:brainstorming` (established the design)
2. The matching verify skill(s) (audited the design against project conventions) — tiered per the spec's proposed content:
   - **Backend signals only** in the proposed content (e.g. mentions of `ProjectCeres/`, `Program.cs`, EF Core, HTTP status codes, ASP.NET pipeline) → `verify-backend`.
   - **Frontend signals only** (e.g. mentions of `ProjectCeres.Client/`, `shadcn`, `base-ui`, Tailwind, design-system primitives) → `verify-frontend`.
   - **Both signals** OR **no detectable signals** (default-safe) → BOTH `verify-backend` AND `verify-frontend`. Invoking the legacy `verify-against-codebase` router satisfies both.

**Enforcement.** HARD via `playbook/hooks/pre-spec-write-gate.js`. **Replaces** the existing `require-verify-against-codebase-before-spec.js` (whose scope is a subset of this gate). Bypass: the file already exists (rewrite of an existing spec).

**What counts as fired.** A `Skill` tool call with the required skill name recorded in state, OR an explicit mention of `verify-backend` / `verify-frontend` / `verify-against-codebase` in the assistant transcript (preserves the bypass behavior of today's hook).

**Why tiered.** A pure frontend spec (Stage 9.x theme-toggle-shaped work) gains nothing from running through `verify-backend`'s §1–§5 — those steps inspect `Program.cs`, EF models, and API conventions that the spec doesn't touch. A pure backend spec gains nothing from `verify-frontend`'s shadcn / design-system audit. Splitting keeps the audit proportional to the spec without weakening either side. Origin: 2026-05-17 review session after the `8c8aab9` ThemeToggle commit's audit pipeline ran irrelevant backend checks.

**Why HARD not ADVISORY.** The originating motivation for the existing hook was the Stage 6b.1 incident where Claude proposed a homegrown `MfaTicketService` that duplicated framework features. That class of error is high-cost — caught pre-dispatch it's a re-plan; caught mid-execution it's a subagent round trip plus rework. The tiering doesn't weaken that — it just routes to the half of the audit that can catch the relevant class of conflict.

**Consumes / produces.**
- Consumes: approved design from Phase A + spec file path under `docs/superpowers/specs/`.
- Produces: the new spec file (the Write completes).

---

### Phase C — `mid-build` (advisory + HARD claim-gates, plus existing live guards)

**Gating event.** Continuous — fires throughout the build phase via the existing hooks. No additional `playbook` hook needed here; this phase is the existing trio plus two HARD Stop-event gates added 2026-05-18 and 2026-05-20:

1. `deep-fix-mode` — auto-fires on circling signals via `frustration-detect.js`, `loop-fingerprint.js`, `same-target-edit-count.js`.
2. `decision-mode` — auto-fires on decision-asking prompts via `decision-detect.js`.
3. `no-unjustified-deferrals` — auto-fires on deferral language and pushback via `pre-write-deferral.js`, `pushback-detect.js`. Phase D escalates this to HARD.
4. **`verify-runtime-state` (HARD)** — auto-fires on Stop when the assistant's final message asserts a runtime-state value (database row, env var, file contents on disk, process state) without co-located evidence from an actual query. Enforced by `verify-runtime-state/hooks/stop-runtime-state-claim.js`. Three guards (evidence-co-located, explicit-assumption, user-authorization); if all fail, the Stop is blocked with `exit 2`. Origin: 2026-05-18 — assistant claimed `AspNetUsers.TwoFactorEnabled = false` based on the absence of a grep hit in `SeedDevUser.cs`. Code inspection ≠ runtime state.
5. **`fix-interaction` (HARD)** — auto-fires on Stop when the assistant's final message mentions a fix (per the phrase list in `fix-interaction/hooks/detect-fix-mention.js`) AND no `Edit`/`Write`/`MultiEdit` tool call landed in the same turn. Forces an explicit "fix now or document?" → "where?" → action → resume-prior-thread interaction via a five-state machine (`none → fix-or-document → where → documenting/fixing → resume → none`) coordinated by three hooks (`detect-fix-mention.js` on Stop, `await-answer.js` on UserPromptSubmit, `check-resolution.js` on Stop). Five dead-lock defenses (TTL, atomic state writes, fail-open on hook error, topic-shift detection, three-way escape) sourced from primary-source workflow-orchestration literature — see `fix-interaction/references/dead-lock-defenses.md`. FIX-CONTEXT guard skips the hook when the fix landed in the same turn. Per-session bypass: `CERES_SKIP_FIX_INTERACTION_HOOK=1` or `/fix-interaction-reset`. Origin: 2026-05-20 — 10-scenario simulation scored 0/10 on proactive action (every response described the fix instead of doing it); user's framing: *"the important part is not pointing the fix or the issue out, is acting proactively on it."*

**Enforcement.** Items 1-3 are ADVISORY today (Phase D escalates #3 to HARD). Items 4 and 5 are HARD at the Stop event, regardless of which subsystem the claim or fix-mention touches — they're cross-cutting message-shape gates, not phase-specific.

**Consumes / produces.**
- Consumes: in-progress build state (edits, tool calls, user prompts, assistant outgoing messages).
- Produces: advisory `additionalContext` reminders (items 1-3); a hard-block on the Stop event when item 4's evidence guard fails or item 5's FIX-CONTEXT guard fails.

**Item 5 hand-off.** When the user answers "document", the state machine transitions to `where`, then `documenting`. The required `[ ]` line written in the destination doc hands to `no-unjustified-deferrals` (Phase D) if the deferral-phrase regex matches the new content — both gates can fire on the same write, and the well-formed-deferral guard's three fields are the natural completion for a `[ ]` opened by `fix-interaction`.

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

### Phase F — `pre-commit` (HARD, conditional + tiered)

**Gating event.** PreToolUse Bash where the command matches `^\s*git\s+(-C\s+\S+\s+)?commit\b` AND the staged diff contains files matching `\.(cs|ts|tsx|csproj|sln)$`.

**Required chain.** Tiered by what the staged diff actually touches:

- **Backend-only staged diff** (`.cs` / `.csproj` files only, no `.ts` / `.tsx`, no `.sln`) → `verify-backend` must have fired this session against the latest diff.
- **Frontend-only staged diff** (`.ts` / `.tsx` files only, no `.cs` / `.csproj`, no `.sln`) → `verify-frontend` must have fired this session against the latest diff.
- **Mixed diff** (both sides touched) OR **`.sln` present** → BOTH `verify-backend` AND `verify-frontend` must have fired this session. Invoking the legacy `verify-against-codebase` router satisfies both. `.sln` escalates to mixed because solution-file edits can ripple either side.

**Enforcement.** HARD via `playbook/hooks/pre-commit-gate.js`. **Path-based bypass (Option C, resolved 2026-05-17):** pure documentation / config / gitignore diffs (`*.md`, `*.json`, `.gitignore`, `*.txt`) skip the gate. Rationale: the verify skills exist to catch code-convention conflicts — they have nothing meaningful to say about doc-only commits.

**What counts as fired.** State-file check for the required verify skill name(s) invoked AFTER the most recent code Write in this session. The legacy `verify-against-codebase` name counts toward either or both sides (it's now a router). The gate inspects: was the required skill run AFTER the most recent code Write? If yes, allow. If the last code Write was after the last verify-fire, deny with "verify needs to re-run against the latest diff".

**Why tiered.** Same rationale as Phase B's tiering, plus a real-world miss: commit `8c8aab9` (ThemeToggle rewrite, 2026-05-17) had four `.tsx` files staged and the agent paid for a full backend audit + `dotnet test` run that found nothing because there was nothing backend-shaped to find. The tier rule fixes that without giving up the convention-audit guard. The split skills + tiered gate together cap audit cost at the diff's actual surface area. Origin: 2026-05-17 review session.

**Consumes / produces.**
- Consumes: a `git commit` Bash invocation + the current staged diff.
- Produces: either the commit proceeds OR a hard-block naming the unverified files + the required verify skill(s) for this tier.

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

### Phase H — `pre-handoff` (HARD)

**Gating event.** Stop event where the assistant's final message contains a manual-test-handoff fingerprint AND a numbered checklist of ≥5 items. Fingerprints (full list in `verify-runtime-state/hooks/stop-manual-test-handoff.js`):

- "manual tests you have to do" / "things to test in the browser"
- "browser checklist" / "verify these in the browser"
- "what to test on your end" / "steps to manually verify"
- "end-of-batch manual verification" / "UX/UI verification checklist"

**Required chain.** ONE of three guards must pass:

1. **Prerequisite-audit block** — the message has a "Prerequisites" / "Before you start" / "Required UI" section, OR an explicit "I audited each entry point" / "verified each prerequisite exists" marker. The audit confirms that each step's entry point (route, button, email trigger) exists in committed code.
2. **Blocked-step markers** — every step whose entry point is missing is marked with "⚠ blocked — UI not built yet" / "prerequisite missing" / "no SPA flow to reach X". Affected steps can be omitted or kept as future work.
3. **User-authorization waiver** — the user's last message explicitly waives the prerequisite audit ("just give me the list", "I'll figure out the prerequisites").

**Enforcement.** HARD via `verify-runtime-state/hooks/stop-manual-test-handoff.js` (Stop event). If no guard passes, exit 2 + stderr explaining the missing audit, and the Stop is blocked. The hook logs every match to `.claude/state/runtime-state-verify/log.jsonl` (audit-trail discipline).

**What counts as fired.** A Stop event where the message contains the handoff fingerprint AND a guard passes. No state-file entry is required — the hook is purely message-shape-driven.

**Why HARD not ADVISORY.** Origin: 2026-05-18. Assistant drafted a 17-step manual TOTP-flow test list for the user where Step 1 required a TOTP-enabled user, but the SPA has no TOTP enrolment page and the seed user doesn't have `TwoFactorEnabled = true`. The list was uncrawlable. Advisory wouldn't have caught it — the assistant had to actually be denied the Stop to learn the lesson.

**Consumes / produces.**
- Consumes: the assistant's final message + the user's prior message.
- Produces: either the Stop completes OR a hard-block with a named missing guard.

**Per-session bypass.** `CERES_SKIP_RUNTIME_STATE_HOOK=1` (shared with Phase C item 4 — they're siblings under the same skill).

---

## State tracking

**File.** `.claude/state/playbook/<session_id>.json`.

**Shape.**

```json
{
  "fired": ["superpowers:brainstorming", "verify-frontend", "verify-backend", "no-unjustified-deferrals"],
  "fired_with_timestamps": [
    {"skill": "superpowers:brainstorming", "at": "2026-05-17T03:12:01Z", "tool_use_index": 14},
    {"skill": "verify-frontend", "at": "2026-05-17T03:18:33Z", "tool_use_index": 27},
    {"skill": "verify-backend", "at": "2026-05-17T03:19:02Z", "tool_use_index": 28}
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

**Bypass-friendly carve-out** (preserves existing hook behavior): for the verify-skill family, an explicit literal string `verify-backend`, `verify-frontend`, or `verify-against-codebase` in the latest assistant message also counts. The router name `verify-against-codebase` satisfies BOTH backend and frontend requirements (it routes to the appropriate sibling); the specific sibling names satisfy only their respective half. Rationale: the existing spec-write hook scans the transcript for these strings; behavior preservation is non-negotiable to avoid surprise denials.

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

`frontend-orchestrator` owns the frontend domain pipeline (discovery → build → refine → simplify → harden → system maintenance for `ProjectCeres.Client/`). `playbook` covers **cross-cutting cohesion** — the seven phases above plus Phase A′. The two skills are siblings, not nested: `frontend-orchestrator` routes the frontend pipeline; `playbook` enforces the constitution and now also flags when frontend work is in scope (Phase A′).

**Coordination — v2 (2026-05-17).** `playbook` invokes `frontend-orchestrator` indirectly via the Phase A′ advisory hooks. Both directions of detection are wired:

- Prompt-side (`frontend-touch-detect.js`): user's intent contains frontend phrasing → advisory before any tool call.
- Content-side (`frontend-spec-postcheck.js`): spec write contained frontend signals → advisory after the write.

The advisory is suppressed once `frontend-orchestrator` has fired in this session. The two hooks share the same state file used by every other phase (`.claude/state/playbook/<session_id>.json`).

Original v1 note: "No coordination logic between the two skills in v1. If they conflict in practice, the user surfaces it and a follow-up edit reconciles them." — That happened; this is the reconciliation.

---

## Slash-command escape hatch

The constitution does not block slash commands. `/deep-fix`, `/plain`, `/no-defer`, `/verify`, `/playbook` continue to work as today. A future thought: `/playbook` could optionally accept `--bypass <phase>` to record an intentional bypass; not implemented in v1.

---

## Decisions resolved 2026-05-17

The cohesion review at `.claude/skills-cohesion-review.md` §6 lists three open questions that were all answered before the build started:

1. **Phase F bypass** — **Option C, path-based**. Only Bash `git commit` invocations whose staged diff contains `\.(cs|ts|tsx|csproj|sln)$` files trigger the gate. Doc-only commits bypass entirely.
2. **Phase E stage-section boundary** — **Option A, markdown convention**. The next `## ` heading at level-2 depth ends the closing stage's section. Project Ceres roadmaps don't use mid-section `## ` headings; markdown's default reading model is the right default.
3. **`/playbook` slash command** — **yes, add it**. Mirrors `/deep-fix`, `/plain`, `/no-defer`, `/verify` symmetry.
