---
name: state-rehydration
description: Sidecar to playbook that survives context-window compaction. Snapshots operational state (skills fired, last code writes, open deferrals, current spec/plan/roadmap) to disk on every turn AND explicitly on PreCompact. On the next SessionStart with source="compact", surfaces the snapshot to the agent so it can pick up mid-procedure without reconstructing context from a half-visible chat history.
---

# state-rehydration

## 1 — Why this skill exists

Project Ceres conversations sometimes exceed the context window and require `/compact`. The user surfaced this in the 2026-05-17 cohesion-review conversation: "There has been where I had to compact the conversation to keep going and lost the entire conversation." Operational state (which skill was mid-procedure, which AskUserQuestion was open, which approval was pending) is the most expensive thing to lose — it's invisible in the half-summary that survives compaction.

`state-rehydration` is the sidecar that makes compaction survivable. It does NOT chain into the seven `playbook` phases — it runs continuously alongside.

## 2 — Pre-compaction event research (2026-05-17)

**Question:** Does Claude Code emit a pre-compaction hook event, and if so, what's its name and payload?

**Finding: YES, Claude Code emits `PreCompact`.** Confirmed from the official documentation at https://code.claude.com/docs/en/hooks (fetched 2026-05-17).

Details:

- **Event name:** `PreCompact` — fires before context compaction begins.
- **Matcher:** distinguishes `"manual"` (user-run `/compact`) vs `"auto"` (Claude Code's automatic-compaction trigger).
- **Payload (stdin):**
  ```json
  {
    "session_id": "abc123",
    "transcript_path": "/Users/.../.claude/projects/.../transcript.jsonl",
    "cwd": "/Users/...",
    "hook_event_name": "PreCompact",
    "matcher_value": "manual" | "auto"
  }
  ```
- **Decision control:** PreCompact CAN block compaction by returning `decision: "block"`. We deliberately do NOT block — we snapshot and let compaction proceed.
- **Companion event:** `SessionStart` fires with `source: "compact"` when the session resumes after compaction. The detection hook uses this signal.
- **PostCompact** also exists but cannot block; not used by this skill.

**Design choice based on the finding.** The snapshot has TWO writers, in priority order:

1. **Primary — `hooks/snapshot-on-pre-compact.js`** wired on `PreCompact`. Fires explicitly before compaction starts. Reads the playbook state file + the transcript + writes a fresh snapshot. This is the load-bearing path.
2. **Backup — `hooks/snapshot-on-turn-end.js`** wired on PostToolUse (any tool). Fires on every assistant turn. Worst case (e.g. PreCompact didn't fire for some reason): the snapshot is stale by exactly one turn, which is far better than losing everything.

The detection hook (`hooks/detect-compaction.js`) is wired on `SessionStart` and fires only when `source === "compact"`. It reads the snapshot and emits `additionalContext` directing the agent to re-read it before the next non-trivial action.

## 3 — How it works

Three pieces:

- **Snapshot file** at `.claude/state/state-rehydration/<session_id>/snapshot.json` (gitignored — same pattern as the playbook state file).
- **Snapshot writers** — `snapshot-on-pre-compact.js` (PreCompact) + `snapshot-on-turn-end.js` (PostToolUse) both maintain the same file.
- **Compaction detector** — `detect-compaction.js` (SessionStart with matcher `compact`) reads the snapshot and surfaces it to the agent.

The snapshot shape is documented in `references/snapshot-shape.md`.

## 4 — What gets snapshotted

- `skills_fired` — copied from `.claude/state/playbook/<session_id>.json`'s `fired` field. Lets the agent know which gates have been satisfied.
- `last_code_writes` — last 10 entries from `writes_since_last_skill` (or `track-session-writes.js` state if richer). Lets the agent know which files were touched mid-procedure.
- `open_deferrals` — copied from the playbook state file's `open_deferrals` array.
- `current_artifacts` — the most recent open spec under `docs/superpowers/specs/`, most recent plan under `docs/superpowers/plans/`, most recent open roadmap under `docs/roadmap-phase-*.md`. Each path + last-modified ISO timestamp.
- `last_assistant_thought` — first 500 characters of the most recent assistant message in the transcript (best-effort, opportunistic — the PreCompact path has direct access to the transcript).
- `snapshot_timestamp` — ISO-8601 of when the snapshot was written.
- `trigger` — `"PreCompact-manual"` / `"PreCompact-auto"` / `"PostToolUse-turn-end"` so the agent knows how fresh the snapshot is.

See `references/snapshot-shape.md` for the canonical JSON schema.

## 5 — When this skill fires

- **PreCompact** — snapshot-on-pre-compact.js fires. Snapshot is fresh.
- **End of every turn** (PostToolUse, any tool) — snapshot-on-turn-end.js fires. Snapshot stays current.
- **SessionStart with source="compact"** — detect-compaction.js fires. additionalContext directs the agent to re-read the snapshot.

## 6 — Anti-patterns

- **"Block compaction to preserve state."** No — compaction is a legitimate user action. The snapshot survives it, that's the point.
- **"Snapshot the whole transcript."** No — the transcript is already in `transcript_path` after compaction. The snapshot is the OPERATIONAL state (skills, deferrals, open artifacts), not the chat history.
- **"Rehydrate by injecting the snapshot into context."** No — surface a pointer, let the agent read the JSON file. Re-injection conflicts with the agent's normal context-loading behavior.

## 7 — Linked memory

- `feedback_brainstorm_spec_plan_execute_flow` — open-spec / open-plan / open-roadmap is exactly the artifact set this skill preserves.
- `feedback_finished_stages_have_no_unchecked_items` — losing open_deferrals to compaction is how the "left unchecked items on a Done stage" failure shows up post-compaction.

## 8 — Sibling skill

`state-rehydration` is a sidecar to `playbook`. The two share state location (`.claude/state/`) and the same project-scope. They do NOT call each other — the rehydration hooks read the playbook state file directly.
