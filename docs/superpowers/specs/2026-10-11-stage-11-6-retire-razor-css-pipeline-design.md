# Stage 11.6 — Retire the dead Razor CSS pipeline (design)

**Date:** 2026-10-11
**Status:** Design approved in chat; spec under user review.
**Placement:** Stage 11.6 — a follow-on to Stage 11 (Razor + URL cleanup), Batch 4/5 boundary. (User to confirm the number at roadmap-insert time.)
**Supersedes (partial):** ADR-0081 § "two sets of pre-built static assets" — see § 6.

## 1. Why

Stage 11 deleted every Razor `.cshtml` view. But the Razor layer's **Tailwind v3 CSS
build** (`ProjectCeres/Styles/app.css` → `wwwroot/css/site.css`) was left running on every
`dotnet build`. `README.md` already documents it as cruft ("no longer styles anything…
a candidate for removal"): zero `.cshtml`, zero `<link>`, no runtime consumer.

Retiring it also **removes the vulnerable `postcss-selector-parser` transitive dependency**
(Dependabot `GHSA-rj75-hqrm-r3gf`, medium) by deletion rather than override — the fix
(`7.1.6`) is a new major that Tailwind 3.4 excludes (it declares `^6`), so ADR-0079
condition 3 forbids overriding it. Deleting the only lockfile that contains it is the
clean fix.

## 2. Scope

**In:** delete the Razor-layer CSS build and every reference to it, across code, build,
container, hooks, and docs. **Out:** everything else — this is a pure retirement.

### Hard non-goals (regressions this stage must NOT cause)
- **`AddControllersWithViews()` stays** (`Program.cs:28`). It is kept deliberately for the
  antiforgery-filter infrastructure `AutoValidateAntiforgeryTokenAttribute` needs
  (`dotnet/aspnetcore#22189`) — the comment above it says so. Switching to
  `AddControllers()` would break antiforgery. Untouched.
- **The React client's Tailwind v4 pipeline + its Vite `dist` build stay.** The
  `BuildSpaClient` MSBuild target (`ProjectCeres.csproj:52-59`), the Dockerfile SPA stage,
  and the `wwwroot/dist` copy are the SPA — all untouched. Only the **separate**
  `BuildTailwind` target and the `site.css` copy go.
- **The SPA's fonts stay.** Confirmed self-contained: the client imports
  `@fontsource-variable/inter` + `@fontsource/ibm-plex-mono` (npm, bundled by Vite —
  `ProjectCeres.Client/src/index.css:4-5`, `package.json:25-26`). It does **not** use
  `wwwroot/fonts/*.ttf`. So `wwwroot/fonts/` is orphaned with the Razor CSS and safe to
  delete. **Verify at implementation** with a runtime font-render check (boot the app / SPA
  smoke) before treating the font deletion as confirmed.

## 3. What gets deleted / changed

| # | Target | Action |
|---|---|---|
| 3.1 | `ProjectCeres/Styles/app.css` | delete (the 245-line dead stylesheet) |
| 3.2 | `ProjectCeres/tailwind.config.js` | delete (Razor Tailwind config) |
| 3.3 | `ProjectCeres/package.json` | delete (holds `tailwindcss` dep + the `postcss`/`postcss-selector-parser` overrides + `build:css`/`watch:css` scripts) |
| 3.4 | `ProjectCeres/pnpm-lock.yaml` | delete (the lockfile that carries `postcss-selector-parser`) |
| 3.5 | `ProjectCeres/node_modules/` | ensure gitignored/removed locally (not committed anyway) |
| 3.6 | `ProjectCeres.csproj` `BuildTailwind` target (L42-47) + its `SkipTailwind` comment (L39-41) | delete the target. **Leave `BuildSpaClient` (L52-59) and its `SkipSpaBuild` untouched.** |
| 3.7 | `Dockerfile` SPA-stage Tailwind block (`COPY ProjectCeres/package.json pnpm-lock.yaml` → `pnpm install` → `COPY ProjectCeres/` → `pnpm run build:css`, ~L16-22) | delete the block |
| 3.8 | `Dockerfile` `COPY --from=spa /src/ProjectCeres/wwwroot/css/site.css …` (L40) | delete the line |
| 3.9 | `Dockerfile` `dotnet publish … -p:SkipTailwind=true` (L45) | drop **only** `-p:SkipTailwind=true`; keep `-p:SkipSpaBuild=true` |
| 3.10 | `.claude/hooks/surface-build-warnings.js` `site-css-size-floor` check | delete that check (it reads `site.css`, which won't exist) + its test in `__tests__/surface-build-warnings.test.js` |
| 3.11 | `wwwroot/fonts/` (inter + ibm-plex-mono ttf) | delete (orphaned; SPA uses `@fontsource/*`) — gated on the § 2 runtime font check |
| 3.12 | `.gitignore` line `ProjectCeres/wwwroot/css/site.css` (L120) | delete |
| 3.13 | `wwwroot/css/site.css` (built artifact) | delete the local build output |

### Documentation (same commit / stage)
| # | Doc | Change |
|---|---|---|
| 3.14 | `CLAUDE.md` § Tech Stack "CSS (Razor layer)" bullet | delete the bullet (the v3 pipeline no longer exists) |
| 3.15 | `README.md` "Legacy CSS pipeline" (L15) + "Known cruft" (L157) | delete both (cruft removed, not documented) |
| 3.16 | `ci.yml` comments referencing `build:css` / `SkipTailwind` / `site.css` (L28, L84, L155) | reword so they no longer describe a removed target |
| 3.17 | `docs/runbooks/ci-actions-troubleshooting.md` (L95-96) | remove the `SkipTailwind: "true"` CI fix (the target it guards is gone) |
| 3.18 | **New ADR** (next free number — grep `docs/decisions/` for the highest) | ADR recording the retirement, with `Supersedes: ADR-0081 (partial)` front matter — see § 6 |

## 4. ADR-0079 reconciliation (the Dependabot tie-in)
ADR-0079 lists `postcss-selector-parser` (`ProjectCeres/package.json`) as a live override.
After this stage that override and its lockfile are gone. Update ADR-0079's Razor-layer
override table + its running advisory log: `postcss-selector-parser` (and the Razor
`postcss` override) are **removed, fixed-by-retirement** — note that the Dependabot alert
`GHSA-rj75-hqrm-r3gf` is resolved by deleting the lockfile, not by a pin. The `braces`
acceptance (Client-side, `ignoreGhsas`) is untouched by this stage.

## 5. Verification (ship gate)
- `dotnet build ProjectCeres.sln` — 0 errors with `BuildTailwind` gone (nothing depends on
  `site.css` existing at build time except the now-deleted hook check).
- `dotnet test ProjectCeres.sln` — full suite green (no behavioural change expected; this is
  the regression net).
- `docker build .` — the image builds with the Tailwind block + `site.css` COPY removed and
  `-p:SkipTailwind` dropped. Boot the container; `GET /` returns the SPA shell `200`.
- **Runtime font check** (§ 2): boot the app (or the staged SPA) and confirm the UI renders
  in Inter / IBM Plex Mono — proving the SPA's `@fontsource/*` path is the real source and
  `wwwroot/fonts/` deletion caused no regression. If fonts break, `wwwroot/fonts/` stays and
  the spec is amended.
- `grep -rn "site.css\|build:css\|SkipTailwind\|BuildTailwind\|wwwroot/fonts" .
  --exclude-dir=node_modules --exclude-dir=.git` returns only intentional historical
  references (ADR history lines, this spec, roadmap close-out) — no live target, no stale
  flag.
- `.claude/hooks/__tests__/surface-build-warnings.test.js` still green with the
  `site-css-size-floor` check + its test removed.
- Dependabot: after the lockfile is gone, `GHSA-rj75-hqrm-r3gf` no longer has a source in
  the repo (dismiss the alert with "fixed by retiring the Razor CSS pipeline, Stage 11.6").

## 6. ADR-0081 supersession (mandatory sweep)
ADR-0081 (Docker containerization) records as decision #1 that "the app serves **two** sets
of pre-built static assets" (SPA + Tailwind `site.css`) and describes `BuildTailwind` /
`SkipTailwind`. This stage reduces that to **one** (SPA only). Per the sync-docs supersession
rule, authoring the new ADR with `Supersedes: ADR-0081 (partial — the Tailwind/site.css half)`
requires sweeping `docs/` for every reference to the Tailwind-asset half and updating each:
- `docs/decisions/ADR-0081-*.md` — add a "Superseded in part by [new ADR]" note on the
  Tailwind/`site.css`/`SkipTailwind` lines (do NOT touch the SPA half).
- `docs/superpowers/plans/2026-09-11-stage-12-14-docker-containerization.md` — it's a
  session artifact (not edited per sync-docs rules) but note it references the old shape.
- `docs/runbooks/ci-actions-troubleshooting.md`, `ci.yml` comments — per § 3.
- `README.md`, `CLAUDE.md` — per § 3.
- `roadmap-phase-three.md` Stage 12.14 close-out lines mentioning "SPA + Tailwind CSS" — add
  a pointer that the Tailwind half was retired in 11.6.
Re-run the `grep` from § 5 after edits; every remaining match must be an intentional
historical reference.

## 7. Risks
- **Docker build is the real integration risk** — `site.css`'s `COPY` is a hard dependency;
  removing the producing block AND the COPY must be done together or the image build fails at
  L40. The plan sequences both in one step + a `docker build` verification.
- **A hidden `/fonts/` or `/css/site.css` consumer** — mitigated by the § 5 grep + the
  runtime font/boot check. Low: the audit found none.
- **CI pnpm-less jobs** — CI used `SkipTailwind` to avoid the pnpm shell-out. With the target
  gone there's nothing to skip; the CI comments explaining the flag become stale and are
  reworded (§ 3.16). No CI *job* depends on `site.css` being produced.
