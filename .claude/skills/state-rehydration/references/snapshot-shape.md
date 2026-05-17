# snapshot.json — canonical shape

Location: `.claude/state/state-rehydration/<session_id>/snapshot.json` (gitignored).

## Schema

```json
{
  "session_id": "abc123",
  "snapshot_timestamp": "2026-05-17T04:12:33Z",
  "trigger": "PreCompact-manual",
  "skills_fired": [
    "superpowers:brainstorming",
    "verify-against-codebase",
    "no-unjustified-deferrals"
  ],
  "last_code_writes": [
    { "file": "ProjectCeres/Common/Authentication/EmailChangeService.cs", "at": "2026-05-17T04:09:01Z" }
  ],
  "open_deferrals": [
    {
      "file": "docs/roadmap-phase-three.md",
      "stage": "Stage 6.15",
      "added_at_tool_use_index": 39,
      "tripwire": "FIXME"
    }
  ],
  "current_artifacts": {
    "open_spec": {
      "path": "docs/superpowers/specs/2026-05-17-playbook-skill-build-design.md",
      "last_modified": "2026-05-17T03:45:01Z"
    },
    "open_plan": {
      "path": "docs/superpowers/plans/2026-05-17-playbook-skill-build.md",
      "last_modified": "2026-05-17T03:50:12Z"
    },
    "open_roadmap": {
      "path": "docs/roadmap-phase-three.md",
      "last_modified": "2026-05-17T04:00:00Z"
    }
  },
  "last_assistant_thought": "First 500 characters of the most recent assistant message in the transcript, opportunistic — may be empty on the PostToolUse path if the transcript isn't yet flushed.",
  "snapshot_version": 1
}
```

## Field reference

| Field | Source | Notes |
|---|---|---|
| `session_id` | hook stdin payload | always present |
| `snapshot_timestamp` | `new Date().toISOString()` | when this snapshot was written |
| `trigger` | hook context | `PreCompact-manual`, `PreCompact-auto`, or `PostToolUse-turn-end` |
| `skills_fired` | `.claude/state/playbook/<session_id>.json` → `fired` | copied verbatim |
| `last_code_writes` | playbook state's `writes_since_last_skill` (fallback: `.claude/state/run-tests/<session_id>.json` `files`) | up to 10 most recent |
| `open_deferrals` | playbook state's `open_deferrals` | copied verbatim |
| `current_artifacts` | filesystem scan of `docs/superpowers/specs/`, `docs/superpowers/plans/`, `docs/roadmap-phase-*.md` | most recent by mtime |
| `last_assistant_thought` | transcript_path tail (PreCompact path only) | best-effort, may be empty |
| `snapshot_version` | constant `1` | bump if shape changes |

## Recovery semantics

When `detect-compaction.js` fires on SessionStart with `source: "compact"`, it reads this file and emits `additionalContext` containing:

1. The snapshot's `trigger` and `snapshot_timestamp` (so the agent knows freshness).
2. A summary line per top-level field (e.g. "skills_fired: 3 entries"; "open_deferrals: 1 entry referencing Stage 6.15").
3. A pointer to the file path so the agent can read the full JSON if needed.

The agent is responsible for reading the file and deciding what to act on. The hook does NOT auto-restore state to context — it surfaces the pointer.

## Forward compatibility

If a future edit changes the shape, bump `snapshot_version` and add a comment in `detect-compaction.js` describing what to do with old-version snapshots. The simplest fallback is to surface "old-version snapshot detected; manual review needed" and let the user decide.
