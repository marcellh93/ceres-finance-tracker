# Stage 11.6 — Retire the Dead Razor CSS Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the dead Razor-layer Tailwind v3 CSS build (`Styles/app.css` → `wwwroot/css/site.css`) and every reference to it across code, build, container, hooks, and docs — which also removes the vulnerable `postcss-selector-parser` transitive dependency by deletion.

**Architecture:** Pure retirement, no new code. Remove the `ProjectCeres/` pnpm project + its Tailwind config + stylesheet + fonts, the `BuildTailwind` MSBuild target, the Dockerfile Tailwind build block + `site.css` copy, two Razor-CSS Stop-hook checks, and the stale flags/comments in CI, runbooks, CLAUDE.md, README. Partial supersession of ADR-0081. The React SPA pipeline and `AddControllersWithViews()` are untouched.

**Tech Stack:** .NET 10 MSBuild, Docker multi-stage build, pnpm (the project being deleted), a Node Stop-hook + its `node:test` suite.

**Spec:** `docs/superpowers/specs/2026-10-11-stage-11-6-retire-razor-css-pipeline-design.md`

## Global Constraints

- **`AddControllersWithViews()` (`Program.cs:28`) MUST stay** — kept for antiforgery (`dotnet/aspnetcore#22189`). Do not change it to `AddControllers()`.
- **The React SPA pipeline MUST stay** — `BuildSpaClient` target (`ProjectCeres.csproj:52-59`), the Dockerfile SPA stage, `wwwroot/dist`, `-p:SkipSpaBuild=true`, and `@fontsource/*` are all untouched. Only the **separate** `BuildTailwind` target and the `site.css` half go.
- **`wwwroot/fonts/` deletion is gated on a runtime font-render check** (Task 5) — the SPA imports `@fontsource-variable/inter` + `@fontsource/ibm-plex-mono` (npm, bundled by Vite); confirm the UI still renders those before treating the deletion as final.
- Commit straight to `main`, NO `Co-Authored-By:` trailer.
- `ProjectCeres/node_modules` is gitignored (not tracked) — no git action; local-only cleanup.
- The generic `browserslist-outdated` hook check stays (it serves both pnpm projects); only the two Razor-CSS-specific checks go.

## Review Focus

This is a deletion stage; the failure modes are "something still needs what we deleted." The spec implies these; each is pinned by a verification step in the owning task, not a unit test (there is no new product code to unit-test):

- **Docker build breaks at the `site.css` COPY** if the producing block is removed but the COPY isn't (or vice versa) — pinned by Task 3's `docker build` run.
- **A runtime 404 on `/css/site.css` or `/fonts/*`** from a stray `<link>` or static reference the audit missed — pinned by Task 5's app-boot + grep sweep.
- **`dotnet build` fails** because something depends on `site.css` existing at build time — pinned by Task 1's build run (the only build-time reader was the hook check, removed in Task 4).
- **CI pnpm-less jobs break** now that `SkipTailwind` names a removed target — pinned by Task 6 (the flag's removal is inert: a non-existent target can't run; CI comments are reworded, not load-bearing).
- **A stale doc/flag points at the deleted target** — pinned by Task 7's repo-wide grep (only historical ADR references may remain).

---

## Task 1: Delete the Razor CSS pnpm project + the BuildTailwind target

**Files:**
- Delete: `ProjectCeres/Styles/app.css`, `ProjectCeres/tailwind.config.js`, `ProjectCeres/package.json`, `ProjectCeres/pnpm-lock.yaml`, `ProjectCeres/wwwroot/css/site.css`
- Modify: `ProjectCeres/ProjectCeres.csproj` (remove the `BuildTailwind` target + its comment, L39-47)
- Modify: `.gitignore` (remove L120 `ProjectCeres/wwwroot/css/site.css`)

- [ ] **Step 1: Delete the pnpm project + stylesheet + config + built artifact**

```bash
git rm ProjectCeres/Styles/app.css ProjectCeres/tailwind.config.js \
       ProjectCeres/package.json ProjectCeres/pnpm-lock.yaml
rm -rf ProjectCeres/node_modules   # gitignored; local cleanup only
rm -f ProjectCeres/wwwroot/css/site.css   # gitignored build output
rmdir ProjectCeres/wwwroot/css 2>/dev/null || true  # if now empty
rmdir ProjectCeres/Styles 2>/dev/null || true
```

- [ ] **Step 2: Remove the `BuildTailwind` MSBuild target from `ProjectCeres.csproj`**

Delete lines 39-47 — the `<!-- SkipTailwind opt-out ... -->` comment and the entire
`<Target Name="BuildTailwind" ...>...</Target>` block. **Leave the `BuildSpaClient` target
(L52-59) exactly as is** — it builds the React SPA. The exact block to delete:

```xml
  <!-- SkipTailwind opt-out: lets a pnpm-less context (e.g. CI's `dotnet ef
       database update`, which only needs the assembly to compile) build
       without the Tailwind CLI. Normal builds still produce site.css. -->
  <Target Name="BuildTailwind" BeforeTargets="Build" Condition="'$(SkipTailwind)' != 'true'">
    <!-- Tailwind v3.4 vendors caniuse-lite inside peers/index.js; it cannot be
         refreshed via an external package bump, so it always warns "outdated".
         Suppress with browserslist's documented switch. See ADR/runbook. -->
    <Exec Command="pnpm run build:css" WorkingDirectory="$(ProjectDir)" EnvironmentVariables="BROWSERSLIST_IGNORE_OLD_DATA=1" />
  </Target>
```

- [ ] **Step 3: Remove the `.gitignore` line**

Delete `.gitignore:120` — `ProjectCeres/wwwroot/css/site.css`.

- [ ] **Step 4: Verify `dotnet build` works with the target gone**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj --nologo`
Expected: Build succeeded, 0 errors. (Nothing depended on `site.css` at build time except
the hook check, removed in Task 4. The build no longer shells out to pnpm for CSS.)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore(11.6): delete the Razor-layer Tailwind CSS project + BuildTailwind target"
```

---

## Task 2: Verify the server test suite is green after the project deletion

**Files:** none (verification task).

**Why its own task:** deleting a pnpm project + an MSBuild target is the kind of change that
can break the build graph in a way the unit build (Task 1 step 4) doesn't surface — e.g. the
solution-level build, or a test-project reference. This is the regression gate before
touching the container.

- [ ] **Step 1: Solution build**

Run: `dotnet build ProjectCeres.sln --nologo`
Expected: Build succeeded, 0 errors across all projects.

- [ ] **Step 2: Full server suite**

Run: `dotnet test ProjectCeres.sln` (foreground, once — do not run concurrently with the Stop hook).
Expected: all green (Analyzers, Tests, Specs). No behavioural change expected — this is the
net. If anything is red, it must be root-caused here before proceeding (not deferred).

- [ ] **Step 3: Commit** (if any incidental fix was needed; otherwise skip — Task 2 writes no files)

No commit if nothing changed; the green run is the deliverable recorded in the task report.

---

## Task 3: Remove the Tailwind pipeline from the Dockerfile

**Files:**
- Modify: `Dockerfile` (remove the SPA-stage Tailwind block L16-22; remove the `site.css` COPY L40; drop `-p:SkipTailwind=true` from L45)

**Interfaces:**
- Consumes: Task 1 (the `build:css` script + `site.css` no longer exist, so the Dockerfile must not reference them).

- [ ] **Step 1: Delete the SPA-stage Tailwind build block**

Remove these lines from the `spa` stage (the comment + the four commands):

```dockerfile
# Tailwind (ProjectCeres): build:css -> wwwroot/css/site.css. Needs the Styles/
# input and the views Tailwind scans (its content globs), so copy the project.
COPY ProjectCeres/package.json ProjectCeres/pnpm-lock.yaml ProjectCeres/
RUN cd ProjectCeres && pnpm install --frozen-lockfile
COPY ProjectCeres/ ProjectCeres/
RUN cd ProjectCeres && pnpm run build:css
```

Also update the stage-1 header comment `# ---- Stage 1: build the two pnpm projects (SPA + Tailwind CSS) ----` → `# ---- Stage 1: build the SPA (Vite) ----`.

- [ ] **Step 2: Delete the `site.css` COPY in the build stage**

Remove line 40:

```dockerfile
COPY --from=spa /src/ProjectCeres/wwwroot/css/site.css ProjectCeres/wwwroot/css/site.css
```

- [ ] **Step 3: Drop `-p:SkipTailwind=true` from the publish command**

Change the publish line from:

```dockerfile
      -p:SkipSpaBuild=true -p:SkipTailwind=true
```
to:
```dockerfile
      -p:SkipSpaBuild=true
```

(`SkipTailwind` named a now-deleted target; passing it is inert, but drop it so nothing
references a removed flag. Keep `SkipSpaBuild` — the SPA target still exists.)

- [ ] **Step 4: Verify the image builds + boots**

Run: `docker build -t ceres-11-6-test .`
Expected: build succeeds (no failure at the old COPY line), `test -f .../dist/app.html` passes.
Then (if a local Postgres/boot smoke is available per the project's Docker runbook) boot the
container and confirm `GET /` returns `200 text/html` (the SPA shell). If Docker isn't
available in this environment, say so explicitly and hand this step to the user with the
exact commands.

- [ ] **Step 5: Commit**

```bash
git add Dockerfile
git commit -m "chore(11.6): drop the Tailwind build + site.css copy from the Dockerfile"
```

---

## Task 4: Remove the two Razor-CSS Stop-hook checks + their tests

**Files:**
- Modify: `.claude/hooks/surface-build-warnings.js` (remove the `tailwind-no-utility-classes` and `site-css-size-floor` check objects)
- Modify: `.claude/hooks/__tests__/surface-build-warnings.test.js` (remove the test asserting `tailwind-no-utility-classes` fires, and the `site-css-size-floor` entry in the disk-reading-detector skip list)

**Interfaces:**
- Consumes: Task 1 (`tailwind.config.js` + `site.css` gone, so both checks are dead).

- [ ] **Step 1: Remove the two check objects from the hook**

In `.claude/hooks/surface-build-warnings.js`, delete the two check objects:
- `{ id: "tailwind-no-utility-classes", check: ... }` (references `tailwind.config.js` content glob)
- `{ id: "site-css-size-floor", check: ... }` (reads `wwwroot/css/site.css`)

**Keep** the `browserslist-outdated` check immediately above them — it is generic (both pnpm
projects emit the Browserslist warning), not Razor-specific.

- [ ] **Step 2: Remove the matching test references**

In `.claude/hooks/__tests__/surface-build-warnings.test.js`:
- Delete the test `"tailwind fires when all utility classes were pruned"` (~L120-121, runs `tailwind-no-utility-classes`).
- In the disk-reading-detector skip list (~L148), remove `"site-css-size-floor"` from `["site-css-size-floor", "stale-claude-lock"]` → `["stale-claude-lock"]`.
- Update the file-header comment (~L12) that cites "the site.css regression it cites" if it names the removed check specifically.

- [ ] **Step 3: Run the hook test suite**

Run: `node --test .claude/hooks/__tests__/surface-build-warnings.test.js`
Expected: all pass, with no reference to the two removed check ids. (Run the file directly —
`node --test <dir>` mis-resolves on this Node version.)

- [ ] **Step 4: Commit**

```bash
git add .claude/hooks/surface-build-warnings.js .claude/hooks/__tests__/surface-build-warnings.test.js
git commit -m "chore(11.6): remove the two Razor-CSS build-warning hook checks"
```

> **Gate-integrity note:** `.claude/hooks/**/*.js` is denied to Edit/Write by
> `pre-gate-integrity-guard.js`. This task edits a gate file, so it needs the user's
> single-factor authorization: the user names `surface-build-warnings.js` (and its test) in
> their message, OR the implementer reports BLOCKED and the controller surfaces it. Do NOT
> attempt to bypass. If blocked, this task's deletions wait for that authorization; Tasks 5-7
> can proceed first.

---

## Task 5: Delete the orphaned fonts (gated on a runtime render check)

**Files:**
- Delete: `ProjectCeres/wwwroot/fonts/inter/`, `ProjectCeres/wwwroot/fonts/ibm-plex-mono/`

**Interfaces:**
- Consumes: Task 1 (`app.css`, the only `/fonts/*` referencer, is gone).

- [ ] **Step 1: Prove nothing live references the fonts**

Run:
```bash
grep -rn "/fonts/\|wwwroot/fonts\|Inter-VariableFont\|IBMPlexMono" . \
  --exclude-dir=node_modules --exclude-dir=.git --exclude-dir=obj --exclude-dir=bin \
  | grep -v "docs/\|CHANGELOG\|\.superpowers/"
```
Expected: no live code/config reference (only this plan/spec/docs). The SPA uses
`@fontsource-variable/inter` + `@fontsource/ibm-plex-mono` (`ProjectCeres.Client/src/index.css:4-5`),
bundled by Vite — not `/fonts/`.

- [ ] **Step 2: Runtime font-render check**

Boot the app (or the staged SPA per the project's run skill) and confirm the UI renders in
Inter / IBM Plex Mono. If browser access is unavailable, stage the SPA (`tools/stage-spa.sh`)
and hand the user a one-line check ("open the dashboard, confirm the body font is Inter, the
numeric font is IBM Plex Mono") with the URL. **If fonts break, STOP — do not delete
`wwwroot/fonts/`; amend the spec.** (They won't, per Step 1, but this is the gate.)

- [ ] **Step 3: Delete the fonts**

```bash
git rm -r ProjectCeres/wwwroot/fonts/inter ProjectCeres/wwwroot/fonts/ibm-plex-mono
rmdir ProjectCeres/wwwroot/fonts 2>/dev/null || true
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore(11.6): delete fonts orphaned with the Razor CSS (SPA uses @fontsource)"
```

---

## Task 6: Reword the stale CI + runbook references

**Files:**
- Modify: `.github/workflows/ci.yml` (comments at L28, L84, L155 referencing `build:css` / `SkipTailwind` / `site.css`)
- Modify: `docs/runbooks/ci-actions-troubleshooting.md` (L95-96, the `SkipTailwind: "true"` CI fix)

**Interfaces:**
- Consumes: Task 1 + Task 3 (the target + flag are gone).

- [ ] **Step 1: Reword the `ci.yml` comments**

The three comments explain *why* CI passed `SkipTailwind` or staged the SPA "not site.css".
With no Tailwind target, reword each so it no longer describes a live flag:
- L28: the comment about `dotnet ef`/provision builds shelling out to `pnpm run build:css` — now they don't (no target). Reword to drop the Tailwind rationale.
- L84: `# ... SkipTailwind is fine — this stages the Vite SPA, not site.css.` → drop the `SkipTailwind`/`site.css` clause; keep the SPA-staging note.
- L155: `# ... not the Razor site.css.` → drop the `site.css` clause.

Do NOT change any CI *step* — only the explanatory comments. No job sets `SkipTailwind` as an
env (verified: it was documented in the runbook as a manual fix, not wired into `ci.yml`).

- [ ] **Step 2: Remove the runbook's `SkipTailwind` fix**

In `docs/runbooks/ci-actions-troubleshooting.md` (L95-96), remove the `SkipTailwind: "true"`
remediation paragraph (the `BuildTailwind` target it guards no longer exists). If it sits in a
numbered list, renumber the remainder.

- [ ] **Step 3: Verify no live `SkipTailwind`/`build:css` reference remains in CI**

Run: `grep -rn "SkipTailwind\|build:css" .github/`
Expected: no matches (comments reworded away). A match means a comment still names the dead flag.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml docs/runbooks/ci-actions-troubleshooting.md
git commit -m "docs(11.6): reword stale CI/runbook references to the removed Tailwind target"
```

---

## Task 7: Docs — CLAUDE.md, README, ADR-0081 supersession, ADR-0079, roadmap, changelog

**Files:**
- Modify: `CLAUDE.md` (§ Tech Stack "CSS (Razor layer)" bullet)
- Modify: `README.md` (L15 "Legacy CSS pipeline", L157 "Known cruft")
- Create: `docs/decisions/ADR-00NN-retire-razor-css-pipeline.md` (next free number; `Supersedes: ADR-0081 (partial)`)
- Modify: `docs/decisions/ADR-0081-docker-containerization.md` (supersession note on the Tailwind/site.css half ONLY)
- Modify: `docs/decisions/ADR-0079-pnpm-overrides-for-transitive-advisories.md` (remove the Razor-layer `postcss-selector-parser`/`postcss` overrides; note fixed-by-retirement)
- Modify: `docs/roadmap-phase-three.md` (new Stage 11.6 section ✅ Done + checklist; Stage 12.14 close-out pointer)
- Modify: `CHANGELOG.md` (`[Unreleased]` entry)

**Interfaces:**
- Consumes: all prior tasks (the deletions they made are what the docs now describe).

- [ ] **Step 1: Run `sync-docs`**

Invoke the `sync-docs` skill against the stage diff. It routes CLAUDE.md / README / the ADRs
and — critically — enforces the **ADR-0081 supersession sweep** (spec § 6): author the new
ADR with `Supersedes: ADR-0081 (partial — the Tailwind/site.css half)`, then grep `docs/` for
every reference to the retired Tailwind-asset half and update each. Do NOT touch ADR-0081's
SPA-asset half. Update ADR-0079's Razor-layer override table: `postcss-selector-parser` +
`postcss` removed, fixed-by-retirement; note `GHSA-rj75-hqrm-r3gf` resolved by deleting the
lockfile.

- [ ] **Step 2: Roadmap — add Stage 11.6 as Done**

Add a `## Stage 11.6 — Retire the dead Razor CSS pipeline ✅ Done` section (in its numbered
slot, physically between Stage 11.5 and the next stage per the file-order rule), with a
verification checklist mapping each spec § 5 item to how it was verified (dotnet build/test,
docker build, font check, grep sweep, hook tests). Add a pointer on the Stage 12.14 close-out
lines that the Tailwind asset half was retired here.

- [ ] **Step 3: Run `changelog-sync`**

Invoke `changelog-sync`; add an `[Unreleased]` entry under a suitable module (e.g. **Tooling**
or **Build**): "Removed the dead Razor-layer Tailwind v3 CSS build (no views consumed it since
Stage 11); this also drops the `postcss-selector-parser` advisory by deleting the dependency."

- [ ] **Step 4: Final grep sweep (spec § 5)**

Run:
```bash
grep -rn "site.css\|build:css\|SkipTailwind\|BuildTailwind\|wwwroot/fonts\|Styles/app.css" . \
  --exclude-dir=node_modules --exclude-dir=.git --exclude-dir=obj --exclude-dir=bin
```
Expected: only intentional historical references remain (ADR-0081's superseded-note lines,
ADR-0079's history log, this spec/plan, the roadmap close-out, CHANGELOG). No live target, no
stale flag, no code/config consumer.

- [ ] **Step 5: Commit**

```bash
git add docs/ CLAUDE.md README.md CHANGELOG.md
git commit -m "docs(11.6): retire Razor CSS pipeline — ADR + supersession sweep + roadmap + changelog"
```

- [ ] **Step 6: Dismiss the Dependabot alert**

After the lockfile is gone, `GHSA-rj75-hqrm-r3gf` has no source in the repo. Dismiss the
GitHub alert via `gh` with reason "fixed — retired the Razor CSS pipeline (Stage 11.6), the
lockfile carrying `postcss-selector-parser` is deleted". (Hand to the user if `gh` alert
dismissal needs their account; surface the exact command.)

---

## Self-Review

**Spec coverage:**
- §3.1-3.5 (delete project/stylesheet/config/lockfile/node_modules) → Task 1. ✓
- §3.6 (BuildTailwind target, keep BuildSpaClient) → Task 1 Step 2. ✓
- §3.7-3.9 (Dockerfile block + COPY + SkipTailwind flag) → Task 3. ✓
- §3.10 (hook check + test) → Task 4 (both checks: `tailwind-no-utility-classes` + `site-css-size-floor`). ✓
- §3.11 (fonts, gated) → Task 5. ✓
- §3.12-3.13 (.gitignore, built site.css) → Task 1 Steps 1,3. ✓
- §3.14-3.17 (CLAUDE.md, README, CI comments, runbook) → Tasks 6, 7. ✓
- §3.18 + §6 (new ADR + ADR-0081 supersession sweep) → Task 7 Step 1. ✓
- §4 (ADR-0079 reconciliation) → Task 7 Step 1. ✓
- §5 (verification) → distributed: dotnet build/test (Tasks 1,2), docker (3), font check + grep (5,7). ✓

**Placeholder scan:** no TBD/TODO. The "next free ADR number" and "if Docker/browser unavailable, hand to user" are explicit runtime conditions with named fallbacks, not placeholders.

**Type consistency:** n/a — no new code/types. The only cross-task dependency is "Task 1 deleted X, so Task N must not reference X," enumerated in each task's Consumes block.

**Review Focus:** the five deletion failure modes are in the Review Focus section, each pinned to a task's verification step (Docker→T3, 404/fonts→T5, dotnet build→T1, CI flag→T6, stale doc→T7). No new product code → no unit tests to add; the gates are build/boot/grep verifications, which is correct for a retirement.
