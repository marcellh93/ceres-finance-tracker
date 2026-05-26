# Evidence Bundle Directory

Per-session state for the **evidence-bundle Stop gate** (skill: `verify-stage-completeness`, hook: `evidence-bundle-check.js`). Not source — gitignored under `.claude/state/`.

## Layout

```
.claude/state/evidence/
  stage-<id>/             # one directory per stage that produced code commits
    walk-summary.json
    trace.zip
    console.json
    network.json
    curl-transcript.txt
    rls-audit.psql
    build-matrix.json
    registry-sweep.json
    turn-shape.json
  _orphan/log.jsonl       # commits with no detectable stage-id
  _log.jsonl              # every hook run (allow / block / bypass)
```

Stage ID is parsed from the conventional-commit scope on `HEAD` (`feat(9.5a):`, `fix(9.3):`, etc.). If none is found in the last 5 commits, the run is logged to `_orphan/log.jsonl` and the gate exits 0.

## Which slots are required for which diff shapes

The hook checks the file list from `git diff --name-only HEAD~1 HEAD` against the table below. **Docs-only / config-only** diffs (anything matching `^docs/|^\.claude/|^README\.md$|\.gitignore$|.*\.md$`) skip the gate entirely.

| Slot | Required when the diff touches… |
|---|---|
| `walk-summary.json` | `ProjectCeres.Client/src/**` or `ProjectCeres/Controllers/**` |
| `trace.zip` | same |
| `console.json` | same |
| `network.json` | same |
| `curl-transcript.txt` | `ProjectCeres/Controllers/**` or `ProjectCeres/Endpoints/**` |
| `rls-audit.psql` | `ProjectCeres/Models/**` (IUserOwned) or `ProjectCeres/Migrations/**` |
| `registry-sweep.json` | `ProjectCeres/Models/**`, `Common/UserOwnedTables.cs`, `Resources/**`, `Common/Email/EmailTemplateKey.cs`, or `Models/AuditLog.cs` |
| `build-matrix.json` | always required when code changes |
| `turn-shape.json` | always required when code changes |

## Freshness

Each slot file must have `mtime > turn-start`. Turn-start is read from `.claude/state/run-tests/last.log`'s mtime (or `HEAD~1` commit time as fallback). A slot present but stale is flagged differently from a slot missing — "stale" means the prior turn's artifact is being passed off as evidence for this turn.

## `turn-shape.json` schema

The hook does not generate this file; a sibling task does. It validates the schema and the co-location flags.

```json
{
  "stage_id": "9.5a",
  "turn_id": "<hash or timestamp>",
  "fix_mentions": [
    { "assistant_message_offset": 1234, "snippet": "the fix is...", "co_located_edit": true, "edit_file": "ProjectCeres/.../File.cs" }
  ],
  "confidence_claims": [
    { "assistant_message_offset": 2345, "snippet": "root cause is...", "co_located_research": true, "research_tool": "WebFetch" }
  ],
  "runtime_assertions": [
    { "assistant_message_offset": 3456, "snippet": "TwoFactorEnabled is false", "co_located_query": true, "query_output": ".claude/state/evidence/.../psql-twofactor.txt" }
  ]
}
```

Every entry in `fix_mentions` must carry `co_located_edit: true`. Every `confidence_claims` entry must carry `co_located_research: true`. Every `runtime_assertions` entry must carry `co_located_query: true` AND a `query_output` path that exists on disk. Any false flag is a gap.

## Bypass

`CERES_SKIP_EVIDENCE_BUNDLE_HOOK=1` exits 0 unconditionally and logs the bypass to `_log.jsonl` for audit.
