# ADR-0084 — Retire the dead Razor-layer Tailwind CSS pipeline

> **Diataxis type:** Explanation — architectural decision record.

**Status:** Accepted — 2026-10-11 (Stage 11.6).

**Phase:** Phase 3 (Hosted Beta).

**Supersedes:** ADR-0081 (partial — the Tailwind/`site.css` asset half only; the SPA-asset half stands).

**Related decisions:** ADR-0081 (Docker containerization — two pre-built asset sets); ADR-0079 (pnpm overrides for transitive advisories); Stage 11 (Razor + URL cleanup — deleted the `.cshtml` views).

## Context

Stage 11 removed every Razor `.cshtml` view when the app went SPA-only. But the Razor
layer's **Tailwind v3 CSS build** — `ProjectCeres/Styles/app.css` compiled to
`wwwroot/css/site.css` by a `BuildTailwind` MSBuild pre-build target shelling out to
`pnpm run build:css` — was left running on every `dotnet build`. It had no consumer: no
`.cshtml`, no `<link>`, no static reference. `README.md` already documented it as cruft.

Two costs made it worth removing rather than tolerating:

1. **A dead build step on every compile.** `BuildTailwind` ran pnpm on every `dotnet build`,
   needed a `ProjectCeres/node_modules`, and forced CI jobs to carry a `SkipTailwind` opt-out
   to build without Node. It produced a 35 KB stylesheet nothing served.
2. **A live dependency advisory with no clean fix.** The `ProjectCeres/pnpm-lock.yaml`
   carried `postcss-selector-parser`, flagged by Dependabot `GHSA-rj75-hqrm-r3gf` (medium).
   The patched version (`7.1.6`) is a new major that Tailwind 3.4 excludes — it declares
   `postcss-selector-parser: ^6.1.2` — so a `pnpm.overrides` pin to `^7.1.6` would violate
   ADR-0079 condition 3 ("the pinned version stays inside the range the parent declares").
   The advisory had no in-range remediation while the pipeline existed.

The React SPA has its own, separate Tailwind v4 pipeline (`ProjectCeres.Client`, Vite) and
bundles its own fonts via `@fontsource-variable/inter` + `@fontsource/ibm-plex-mono` — it
does not depend on the Razor pipeline or on `wwwroot/fonts/`.

## Decision

Delete the Razor-layer CSS pipeline in full: `Styles/app.css`, `tailwind.config.js`,
`ProjectCeres/package.json` (+ its `postcss`/`postcss-selector-parser` overrides) and
`pnpm-lock.yaml`, the `BuildTailwind` MSBuild target, the Dockerfile Tailwind build block +
the `site.css` COPY + the `-p:SkipTailwind` publish flag, the two Razor-CSS build-warning
Stop-hook checks, the orphaned `wwwroot/fonts/`, the `.gitignore` `site.css` line, and the
`SkipTailwind` env/comments in CI + the runbook.

**Kept deliberately:**
- **`AddControllersWithViews()`** (`Program.cs`) — for the antiforgery-filter infrastructure
  (`dotnet/aspnetcore#22189`), unrelated to CSS.
- **The entire React SPA pipeline** — `BuildSpaClient`, the Dockerfile SPA stage,
  `wwwroot/dist`, `-p:SkipSpaBuild`, and `@fontsource` fonts.

**Dependency outcome:** `GHSA-rj75-hqrm-r3gf` is resolved **by deletion** — the lockfile that
carried `postcss-selector-parser` no longer exists. This is strictly better than an
acceptance or an out-of-range override. See ADR-0079's updated Razor-layer override table.

## Consequences

- `dotnet build` no longer shells out to pnpm for CSS; CI jobs no longer need `SkipTailwind`.
- The deployment image carries one pre-built asset set (the SPA), not two.
- `postcss-selector-parser` has no source in the repo; the Dependabot alert is dismissed as
  fixed-by-retirement.
- The `braces` advisory (Client-side, `GHSA-vfj7-8cjw-p6xm`) is unaffected — it stays
  accepted via `pnpm.auditConfig.ignoreGhsas` per ADR-0079 (a different project, a dev-only
  tool, no patched version).
- If a future need for server-rendered Razor CSS ever arises, it is a fresh pipeline decision,
  not a revival of this one.
