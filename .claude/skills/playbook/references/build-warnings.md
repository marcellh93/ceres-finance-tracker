# Build-warning corpus — surfaced by `.claude/hooks/surface-build-warnings.js`

Watched on `PostToolUse` for `Bash` commands matching build/test invocations (`dotnet build`, `dotnet test`, `pnpm build`, `pnpm test`, `pnpm --dir ProjectCeres.Client *`). Each warning fires advisory `additionalContext` — no blocking. Per-session deduplication via `.claude/state/surface-build-warnings/<session_id>.json` so the same warning isn't re-surfaced every turn.

## Corpus

| ID | Pattern (regex / check) | Source / motivation | Action |
|---|---|---|---|
| `browserslist-outdated` | `/Browserslist:\s*caniuse-lite is outdated/i` | `feedback_surface_build_pipeline_warnings`; cohesion review §2.3 incident #4 (2026-05-11 session) | `pnpm update caniuse-lite browserslist` (or `pnpm dlx update-browserslist-db@latest`) |
| `tailwind-no-utility-classes` | `/Tailwind CSS:\s*warn.*No utility classes were detected/i` OR `/no utility classes were detected/i` | `feedback_surface_build_pipeline_warnings`; commit `f8616087` (Tailwind glob regression silently shipped 8 KB site.css for ~35 KB expected) | Check `tailwind.config.js` `content` glob — Razor `.cshtml` files alone aren't enough; `./Styles/**/*.css` must also be included so `@apply` directives are scanned |
| `site-css-size-floor` | Detects a successful Razor build (look for `wwwroot/css/site.css` write or rebuild) AND a `site.css` file size **below 20 000 bytes**. Detection: after the build command, the hook stats `ProjectCeres/wwwroot/css/site.css` if it exists | Direct lesson from commit `f8616087` — 8 KB was the regression, 35 KB was healthy | "site.css below 20 KB floor — likely Tailwind content-glob regression. Compare `git log -p ProjectCeres/tailwind.config.js`" |
| `stale-claude-lock` | Files matching `.claude/*.lock` with `mtime` > 1 hour old | `f8616087` gitignore addition (`scheduled_tasks.lock` was orphaned from a prior process); cohesion review §2.3 incident #4 | "Stale lock file: `<path>`. Process holding it may have crashed. Investigate before removing" |
| `dotnet-deprecation` | `/warning\s+(NETSDK\d{4}\|CS06\d{2}\|CA\d{4}\|MSB\d{4})/i` (deprecation/obsolete warnings, NOT compile errors) | `feedback_surface_build_pipeline_warnings` | Surface the warning code + the line that emitted it; link to the corresponding diagnostic page |
| `test-runtime-regression` | Detects `dotnet test` run, then parses elapsed time from the run summary. **Threshold: > 6 min** (per `project_test_suite_performance` memory — current baseline is ~3:30, single-run variance ±30%, sustained >6 min = regression) | `project_test_suite_performance`; lessons from `f8616087` (the 8-min sleeps removed there were the regression to revert if test runtime jumps back up) | "Test runtime regression: `<runtime>` exceeds baseline 3:30 by >70%. Check whether `RateLimitedAuthEndpointTests` reverted from `WithFreshRateLimiter()`/`WithShortLoginWindow()` patterns to real-wall-clock `Task.Delay(70s)` calls (see commit `f8616087`)" |

## Adding new warnings

When a new warning class is observed and surfaced manually, add a row to this table with:
- A unique ID (kebab-case, short).
- The regex or check that detects it.
- The source (memory entry / cohesion-review incident / commit SHA).
- A specific remediation action (not "investigate" — name the file or command).

The hook reads this file's table on each invocation, so changes here take effect on the next build without modifying the hook code.

## State file shape

`.claude/state/surface-build-warnings/<session_id>.json`:

```json
{
  "surfaced": ["browserslist-outdated", "tailwind-no-utility-classes"]
}
```

Once an ID appears in `surfaced`, the hook will not re-emit that warning for the rest of the session. Deletion of the state file (or session change) resets the dedup.

## What this hook does NOT do

- **It does not block.** Every warning is advisory `additionalContext`. The build/test invocation that fired completes normally; the warning surfaces on the next agent turn.
- **It does not fix.** The remediation column tells the agent what to do; the hook itself never runs the fix command.
- **It does not watch `git build`, `make`, generic shell invocations, or test commands run inside the IDE**. The matcher is a deliberate small set — `dotnet build`, `dotnet test`, `pnpm build`, `pnpm test`, `pnpm --dir ProjectCeres.Client`. Expanding the matcher is fine; do it explicitly.
- **It does not call the `surface-build-warnings` ID a "skill" or "playbook chain link".** This is a sidecar hook with a corpus, not a routing decision. The corpus lives under `playbook/references/` because the playbook directory is the natural home for cross-cutting cohesion concerns.
