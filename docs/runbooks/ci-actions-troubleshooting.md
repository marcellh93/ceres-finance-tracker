# CI / GitHub Actions troubleshooting

## Standing posture: handle CI yourself with `gh` — don't wait to be told

`gh` is authenticated in this environment, so a failing CI run is **yours to
diagnose and fix without waiting for the user to paste logs or screenshots.**
After any push, watch the run, read a failing job's real log, fix, and re-push —
the same way you would iterate on a local test failure. Asking the user "did CI
pass?" or "can you send me the log?" is the anti-pattern this runbook exists to
end: every screenshot round-trip is a turn the `gh` commands below would have
saved. The only step that ever needs the user is a one-time `! gh auth login` if
`gh` reports it is not authenticated. CLAUDE.md § *Handling CI failures* is the
short version of this; the commands and gotchas below are the detail.

## The one rule that governs all of this: read the actual failure output FIRST

When a build, a test run, or a CI job fails, the **first action is to read the
real failure output** — the specific failing step's log, the exact error line —
**before forming any hypothesis about the cause.** No "it's probably the ordering
/ the cache / the culture." Read what actually broke, then fix that.

This is written down because it was violated repeatedly on 2026-09-10 during the
Stage 12.13 CI bring-up. The pnpm-on-PATH failure was "fixed" three times on
plausible theories — remove `cache:pnpm`, swap the action order, then swap it back
— each pushed blind, each wrong. The moment `gh` was authenticated and the step
log was actually read, the cause was obvious (`pnpm/action-setup` misparsing the
`packageManager` sha512 hash as a directory path, "installing" a bogus
`pnpm@0.0.0`) and the fix landed first try. Every wasted push was a turn spent
theorizing instead of reading.

The tell that you are about to repeat this: you are editing a file to fix a
failure and you have **not**, in the same session, read the failing step's log.
If that is true, stop and read it first.

## How to read a CI failure (the commands)

`gh` must be authenticated (`gh auth status`; if not, ask the user to run
`! gh auth login`). Then, **read before editing**:

```bash
# 1. Which jobs failed on the latest run?
gh run list --limit 5
gh run view <run-id> --json jobs --jq '.jobs[] | "\(.conclusion)\t\(.name)"'

# 2. THE failing step's log — this is the line you must read before any fix.
gh run view <run-id> --log-failed

# 3. A whole job's log when the failure is swallowed (a script's >/dev/null,
#    a webServer subprocess, an implicit build). --log-failed shows only the
#    step; --log shows everything the step ran.
gh run view <run-id> --job <job-id> --log

# 4. Watch a run you just triggered, to completion, and read it yourself —
#    do not ask the user to screenshot it.
gh run watch <run-id> --interval 25
```

Find a job id: `gh run view <run-id> --json jobs --jq '.jobs[] | select(.name=="dotnet-test") | .databaseId'`.

## Reproduce locally before pushing a fix

A GitHub Actions run only exists on GitHub, so a fix does have to be pushed to be
tested there — but that is the *last* step, not the first. Before pushing:

- **Reproduce the exact failing command locally** where you can. The migration
  that failed in CI was run verbatim against a throwaway local DB; the culture bug
  was reproduced under `LC_ALL=C`; the pnpm-less build was reproduced by stripping
  pnpm from `PATH`. Each reproduction turned a guess into a confirmed cause.
- **Un-silence swallowed errors.** `setup-test-db.sh` had `>/dev/null` on the
  `dotnet ef` call, so a real migration/build failure surfaced only as "exit code
  1". If the log says "Build failed. Use dotnet build to see the errors" or shows a
  bare exit code with no error, the error is being hidden — find and remove the
  redirect, or run the step's `--log` (not `--log-failed`).

## Gotchas this project actually hit (2026-09-10, Stage 12.13)

These are the concrete causes found by reading logs — recorded so the next run
doesn't re-derive them.

- **pnpm not installed / `command not found` despite `action-setup` going green.**
  `pnpm/action-setup@v4` reads the version from `package_json_file`'s
  `packageManager` field. Ours carries a full `pnpm@10.33.2+sha512.<base64 with +
  and />` hash, which the action misparses as a local directory path and
  "installs" a bogus `pnpm@0.0.0` — `PNPM_HOME` then points at an empty dir. Fix:
  pass an explicit `version: "10.33.2"`, never `package_json_file`, for the pin.

- **`error NETSDK1004: Assets file ... project.assets.json not found`** during
  `dotnet ef database update`. `dotnet ef` implicitly builds the project, which
  needs a prior `dotnet restore ProjectCeres/ProjectCeres.csproj`. Add the restore
  step before provisioning.

- **`MSB3073: "pnpm run build:css" exited with code 127`** in a job with no pnpm.
  `ProjectCeres.csproj`'s `BuildTailwind` target runs `pnpm run build:css` on
  *every* build. A pnpm-less job (dotnet-test) or a `dotnet run` subprocess whose
  PATH drops the pnpm shim (e2e webServer) fails it. Fix: set `SkipTailwind: "true"`
  as a job-level env — the `BuildTailwind` target has a `'$(SkipTailwind)' != 'true'`
  condition for exactly this. CSS is irrelevant to the server suite and the E2E
  stylesheet is the Vite-built SPA bundle, not the Razor `site.css`.

- **Tests that pass locally, fail on a clean CI runner.** These are real bugs CI
  exists to catch — the test was passing on leftover local state a fresh runner
  lacks. Seen: a test needing `wwwroot/dist` staged (gitignored; 404 without it —
  stage via `tools/stage-spa.sh`); a test connecting to `project_ceres_e2e` (only
  `project_ceres_test` was provisioned); an invariant-culture email-resource
  regression (CI runs locale `C`, so `IStringLocalizer` returned resource keys as
  email subjects — fixed by defaulting `CultureInfo.DefaultThreadCurrent[UI]Culture`
  to `en` at startup); and a Vitest module-singleton (`useSettings`) leaking a
  fetch across tests, producing an intermittent post-teardown unhandled rejection
  (fixed by seeding + resetting the singleton in global test-setup). Reproduce the
  clean-runner condition locally: `LC_ALL=C`, an empty `wwwroot/dist`, a throwaway
  DB — do not "fix" by leaning on the local leftover.

- **A `$GITHUB_OUTPUT` write failing with "Unable to process file command 'output'".**
  The value written spanned multiple lines (`pnpm ls --parseable` emits two path
  lines). `$GITHUB_OUTPUT` is a single-line `key=value` protocol — write exactly one
  clean value.

## Gotcha (2026-09-11, Stage 12.14): gitleaks license flake

- **`repo-hygiene` fails at "Secret scan (gitleaks)" with `🛑 missing gitleaks
  license` / `API rate limit exceeded ... License key validation will be enforced`
  — and NO secret was actually found.** `gitleaks/gitleaks-action@v3` requires a
  paid `GITLEAKS_LICENSE` for organization accounts and validates it via a GitHub
  API call; when that call hits the rate limit it hard-fails the job. It is a
  transient infra flake, not a finding — the identical step passes on other runs
  and on `gh run rerun <id> --failed`. **This is the textbook re-run-to-confirm
  case** (see § Standing posture): same step green on the 3 prior runs, red on a
  docs-only push, green on re-run.
- **The durable fix (applied 2026-09-11):** don't use the action wrapper. The CI
  step now installs the pinned gitleaks BINARY and runs it directly —
  `gitleaks git . --redact --exit-code 1` (the `git` subcommand scans full history;
  `checkout` uses `fetch-depth: 0`). The binary has no license gate and no
  rate-limited validation call, so the flake cannot recur. To bump the version,
  change `GITLEAKS_VERSION` in `.github/workflows/ci.yml` and confirm the release
  asset name still matches `gitleaks_<v>_linux_x64.tar.gz`.
