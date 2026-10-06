# ADR-0079 — pnpm overrides as the remediation path for transitive npm advisories

> **Diataxis type:** Explanation — architectural decision record.

**Status:** Accepted — 2026-08-10; re-pinned 2026-09-10 (Stage 12.16); `postcss-selector-parser` added 2026-09-12 (Dependabot #68); re-pinned 2026-10-01. Fifteen overrides live in `ProjectCeres.Client/package.json`, two in `ProjectCeres/package.json`.

> **Update 2026-09-10 (Stage 12.16).** A fresh advisory wave (16 alerts, 7 high) outran several existing pins and added new transitive targets. Re-pinned to the newest in-range patched floor (condition 3 re-verified against each parent's registry manifest): `fast-uri` `^3.1.5→^3.1.7`, `js-yaml` `^4.3.1→^4.3.2`, `qs` `^6.15.2→^6.16.0`, `hono` `^4.12.34→^4.13.7`. Added three new overrides — `@hono/node-server` `^1.19.17`, `browserslist` `^4.28.9`, `postcss-selector-parser` `^7.1.6` — and bumped the direct devDependency `vitest` `^4.1.6→^4.1.11` (which also cleared its transitive `@vitest/mocker`). `@hono/node-server` is no longer *left open*: this wave's advisory is fixed in the **1.x** line (`>=1.19.15`, inside the SDK's `^1.19.9` range), unlike the earlier one that needed a 2.x major. Result: **`pnpm audit` fully clean (0 at every severity)**. All four required commands below pass (lint's 5 pre-existing `setState-in-effect` errors are unrelated and tracked separately).

> **Update 2026-10-01.** Dependabot alert #81 (`ip-address` GHSA-2vr4-cq9g-pvrc, moderate — an SSRF-classifier gap in the NAT64 local-use range, CWE-918) landed against the then-current `^10.3.1` pin. Re-pinned to `^10.5.1` (resolves 10.7.2, inside `express-rate-limit`'s declared `^10.2.0` — condition 3). Running `pnpm audit` to verify the fix surfaced three more pins that had drifted below their newest patched floor since the 2026-09-10 wave — none newly introduced, all pre-existing drift: `undici` `^7.29.0→^7.29.1` (10 advisories: WebSocket DoS, TLS-validation bypass via BalancedPool, cross-user cookie disclosure, response splitting/truncation, unsafe-method-response caching — all inside jsdom's `^7.25.0`), `fast-uri` `^3.1.7→^3.1.8` (host-case-normalization bypass, inside ajv's `^3.0.1`), `brace-expansion` `^5.0.7→^5.0.12` (3 stack-exhaustion DoS advisories, inside minimatch's `^5.0.8`). All four re-pinned in the same commit as the `ip-address` fix. Result: **`pnpm audit` fully clean (0 at every severity)**. All four required commands below pass.

> **Update 2026-10-03 (Stage 14 push).** Dependabot alert #83 — `braces` GHSA-vfj7-8cjw-p6xm / CVE-2026-93687 (high — stack-exhaustion DoS via deeply nested brace patterns in the recursive AST walker). Reaches the tree **dev-only** via `shadcn → ts-morph → @ts-morph/common → fast-glob → micromatch → braces@3.0.3`; confirmed **not in the shipped bundle** (`grep braces ProjectCeres.Client/dist/assets/*.js` is empty — it runs only during `pnpm dlx shadcn add` scaffolding, never in the app runtime). This one **cannot be overridden**: the advisory's "Patched versions" is **None** (verified against the GHSA page 2026-09-18 — all versions ≤3.0.3, the latest, are affected; the maintainer has shipped no fix), and the parent (`shadcn`) must stay (`src/index.css` imports `shadcn/tailwind.css`). With no patched version and an unfixable parent, the only honest action is a **documented, scoped acceptance**: added `GHSA-vfj7-8cjw-p6xm` to `pnpm.auditConfig.ignoreGhsas` in `ProjectCeres.Client/package.json` — this suppresses *only this one GHSA*, so the CI audit gate still fails on every other/new advisory. **Re-check trigger:** drop the ignore entry on any `shadcn` upgrade or when `braces` publishes a patched version, and run the removal test below; if the advisory no longer appears, delete the ignore. Accepted-risk rationale: a brace-expansion DoS in a dev-time scaffolding CLI the developer invokes by hand is near-zero real risk and has no available remediation. All four required commands below pass.

> **Update 2026-10-06 (CI `repo-hygiene` audit step failed on a new advisory wave).** Three advisories landed at once: `proxy-addr` GHSA-jqcg-44mw-7w3h (critical — IP spoofing via IPv4-mapped addresses, `>=1.1.0 <2.0.8`, via `shadcn → @modelcontextprotocol/sdk → express`), `source-map-js` GHSA-68fv-2mgg-jv7q (high — event-loop DoS, `<1.2.2`, via `@tailwindcss/node` in the client and `tailwindcss → postcss` in the Razor layer) and `@modelcontextprotocol/sdk` GHSA-6qxp-vccf-f47h (high — OAuth client could send credentials to a server-chosen authorization server, `<1.31.0`, via `shadcn`). **Resolved with no new override:** each patched version sits **inside the range its parent already declares** (`express` `proxy-addr ^2.0.7` → 2.0.8; `@tailwindcss/node` and `postcss` `source-map-js ^1.2.1` → 1.2.2; `shadcn@4.7.0` `@modelcontextprotocol/sdk ^1.26.0` → 1.32.1), so `pnpm update proxy-addr source-map-js @modelcontextprotocol/sdk --depth Infinity` re-resolved exactly those three lockfile entries and nothing else (`package.json` untouched). An override would have added another global pin and ceiling for no benefit — **prefer a targeted lockfile refresh whenever the patched version is already in range, and keep overrides for the case where the parent's range excludes it.** **Premise change worth knowing:** condition 2 above (parent already at latest) is no longer true of `shadcn` — it is now 4.21.3 against the locked 4.7.0 — so a future wave may be fixable by upgrading shadcn instead; that upgrade is a separate, riskier change (it also decides the fate of the `braces` ignore above) and was not taken here. Verified: audit exit 0 (only the already-ignored `braces` remains), and the four required commands (client build, lint, 1181 tests, `dotnet build ProjectCeres/ProjectCeres.csproj`). The Razor-layer lockfile got the `source-map-js` refresh too; Tailwind's compiled CSS is **byte-identical** with the old and new lockfile (A/B run), so the bump changes no output. The Razor lockfile still reports `braces` (no patched version, same acceptance as above; CI does not audit that lockfile) and one moderate left open earlier.

**Phase:** Phase 3 (Hosted Beta) — post-publication hardening, after the repository went public 2026-08-09.

**Supersedes:** None.

**Related decisions:** ADR-0033 (Chart.js as charting library) — the only prior dependency-selection ADR.

## Context

Publishing the repository turned on GitHub Dependabot, which reported **60 advisories (21 high)** against `ProjectCeres.Client/pnpm-lock.yaml` and one against `ProjectCeres/pnpm-lock.yaml`.

Thirteen of those clusters shared a shape that blocked the ordinary fix:

- The vulnerable package was **transitive** — nothing in either `package.json` named it, so there was no version to bump.
- The **parent could not be upgraded**. Six clusters reached the tree through `shadcn@4.7.0`, which is the latest published release; upgrading it was not an option because there was nothing newer. Two more arrived through `jsdom@29.1.1`, likewise current.
- The advisory nonetheless demanded a specific floor version.

`pnpm.overrides` is the only mechanism that resolves this: it instructs pnpm to substitute a version for **every** package in the tree that requests the overridden name, regardless of what that package's own manifest declares.

One finding is worth recording because it shaped the decision. `shadcn` is a scaffolding CLI — `pnpm dlx shadcn add <component>` writes a file and exits — and it is declared as a **runtime dependency**, so it drags `@modelcontextprotocol/sdk` and an entire server stack (`hono`, `express-rate-limit`, `ajv`) into the tree. Removing it from `dependencies` was attempted and reverted: `src/index.css:3` contains `@import "shadcn/tailwind.css"`, a real stylesheet the design system builds on. The package must stay, so its dependency tail must be overridden instead.

## Decision

Use `pnpm.overrides` to pin transitive packages to their patched floor when, and only when:

1. The package is transitive — it is not in `dependencies` or `devDependencies`.
2. Its parent is already at the latest published version, so a normal upgrade cannot reach the fix.
3. The pinned version stays **inside the range the parent declares**, so the override corrects a resolution rather than forcing an incompatibility.

Condition 3 was satisfied for every entry below and is not optional. It is also the condition that stopped one alert from being fixed at all (see *Deliberately left open*). Two examples: `jsdom@29.1.1` declares `undici: ^7.25.0`, so pinning `^7.29.0` stays within its own range — this is why `7.29.0` was chosen over the available `8.x`. `@modelcontextprotocol/sdk` declares `hono: ^4.11.4` and `@hono/node-server` peers `^4`, so `^4.12.34` is inside both.

If an override would violate condition 3, it is not an override — it is an unverified upgrade of someone else's dependency, and the correct action is to wait for the parent or replace it.

## The overrides

### `ProjectCeres.Client/package.json`

| Override | Pinned | Resolves to | Cleared | Reached the tree via |
| --- | --- | --- | --- | --- |
| `hono` | `^4.13.7` | 4.13.7 | 16 | shadcn → @modelcontextprotocol/sdk |
| `undici` | `^7.29.1` | 7.29.1+ | 12 | jsdom (test DOM environment) |
| `postcss` | `^8.5.23` | 8.5.26 | 4 | shadcn; vite |
| `brace-expansion` | `^5.0.12` | 5.0.12+ | 3 | eslint → minimatch |
| `ip-address` | `^10.5.1` | 10.7.2 | 3 | shadcn → @modelcontextprotocol/sdk → express-rate-limit |
| `js-yaml` | `^4.3.2` | 4.3.2 | 3 | shadcn → cosmiconfig |
| `fast-uri` | `^3.1.8` | 3.1.8+ | 3 | shadcn → @modelcontextprotocol/sdk → ajv |
| `nanoid` | `^3.3.18` | 3.3.18 | 2 | postcss (under both shadcn and vite) |
| `qs` | `^6.16.0` | 6.16.0 | 1 | shadcn → @modelcontextprotocol/sdk → express |
| `body-parser` | `^2.3.0` | 2.3.0 | 1 | shadcn → @modelcontextprotocol/sdk → express |
| `@babel/core` | `^7.29.6` | 7.29.7 | 1 | shadcn → @babel/preset-typescript; eslint-plugin-react-hooks |
| `esbuild` | `^0.28.1` | 0.28.2 | 1 | vite; tsx |

### `ProjectCeres/package.json`

| Override | Pinned | Resolves to | Cleared | Reached the tree via |
| --- | --- | --- | --- | --- |
| `postcss` | `^8.5.23` | 8.5.26 | (shared) | tailwindcss 3.4.19 |
| `postcss-selector-parser` | `^6.1.3` | 6.1.4 | Dependabot #68 (low, dev) | tailwindcss 3.4 → postcss-nested |

The Razor-layer entries exist because the postcss-family advisories affected this lockfile too. `postcss-selector-parser` was added 2026-09-12 (Dependabot #68 — uncontrolled AST recursion DoS, `>=6.1.0 <6.1.3`, dev-only): pinned to `^6.1.3` (resolves 6.1.4, inside tailwind/postcss-nested's `^6` range — ADR condition 3). Verified: `pnpm --dir ProjectCeres install --frozen-lockfile` + `run build:css` succeed; `pnpm audit` reports no known vulnerabilities.

**None of the thirteen were reachable from application code.** Each was verified individually — no path in `ProjectCeres.Client/src` parses YAML, IP addresses, or URIs; there is no hono server (CORS is ASP.NET Core's); `undici` serves only jsdom inside the test environment. They were fixed because unreachable-today is not unreachable-forever, and because open high-severity alerts on a public portfolio repository carry their own cost.

Result: **60 advisories → 1** (0 high, 0 critical, 0 low; 1 moderate, deliberately left open).

## Consequences — read this before bumping any of the thirteen

**An override is global. It is not scoped to the dependency that motivated it.**

This is the property that makes overrides work and the property that makes them dangerous. Changing one line changes the resolved version for **every** package in the tree that depends on that name — including packages added months later that nobody checked against the pinned version.

Direct consumers at the time of writing:

| Override | Every package whose request it rewrites |
| --- | --- |
| `postcss` | `@tailwindcss/vite`, `@vitejs/plugin-react`, `@vitest/mocker`, `shadcn`, and (Razor layer) `tailwindcss` |
| `nanoid` | `postcss`, `shadcn`, `vite` |
| `brace-expansion` | `@eslint/config-array`, `@ts-morph/common`, `@typescript-eslint/typescript-estree`, `minimatch` |
| `undici` | `jsdom`, `vitest` |
| `fast-uri` | `ajv`, `ajv-formats`, `@modelcontextprotocol/sdk` |
| `ip-address` | `express-rate-limit`, `@modelcontextprotocol/sdk` |
| `js-yaml` | `cosmiconfig`, `shadcn` |
| `hono` | `@hono/node-server` |
| `@hono/node-server` | `@modelcontextprotocol/sdk` (added 2026-09-10) |
| `browserslist` | `@babel/helper-compilation-targets` → `@babel/core` (added 2026-09-10) |
| `postcss-selector-parser` | `shadcn` (added 2026-09-10) |
| `qs` | `body-parser` |
| `body-parser` | `express`, `express-rate-limit`, `@modelcontextprotocol/sdk` |
| `@babel/core` | `eslint-plugin-react-hooks`, and the `@babel/helper-*` / `@babel/plugin-syntax-*` graph |
| `esbuild` | `vite` (peer), `tsx` |

Concretely: raising the `postcss` pin does not just affect shadcn. It changes the CSS processor used by the Vite build, the React plugin, the Vitest mocker, and the Razor layer's Tailwind v3 pipeline — five consumers, two projects, one line. Raising the `undici` pin changes the HTTP layer under every one of the 1014 client tests.

Three further consequences:

1. **The pin is also a ceiling.** `^8.5.23` means "8.5.23 or newer, within major 8". If a consumer later adopts postcss 9, the override silently holds it at 8. Nothing errors; resolution just quietly stops advancing.
2. **The override outlives its reason.** When a parent finally ships a patched dependency, the override becomes redundant — with no warning, no expiry, and no signal. It persists until someone checks.
3. **Upstream never tested this combination.** `shadcn@4.7.0` was released against `postcss@8.5.14`; it now receives `8.5.26`. The verification that the pairing works is ours, not the maintainers'.

## Verification required when changing any pin

Because the blast radius spans both projects, a pin change is not a lockfile edit — it is a build-affecting change. Run all four:

```bash
pnpm --dir ProjectCeres.Client build     # chunks within budget
pnpm --dir ProjectCeres.Client lint      # exit 0
pnpm --dir ProjectCeres.Client test      # 1014 passed / 165 files
dotnet build ProjectCeres/ProjectCeres.csproj   # exercises the Razor Tailwind pipeline
```

`dotnet build` matters specifically for the `postcss` and `nanoid` pins: the `BuildTailwind` MSBuild target shells out to `pnpm run build:css`, so a broken CSS resolution fails the .NET build, not just the client one.

## Removal test — run periodically, and after any `shadcn` or `jsdom` upgrade

Overrides are debt. The test for whether one can be retired:

1. Comment the entry out of `package.json`.
2. `pnpm install && pnpm audit`.
3. If the advisory does **not** return, the upstream shipped a patched version — delete the override.
4. If it does return, restore the entry.

Eight of the twelve client overrides exist solely because of `shadcn`'s `@modelcontextprotocol/sdk` dependency (`hono`, `ip-address`, `fast-uri`, `js-yaml`, `qs`, `body-parser`, plus `postcss` and `@babel/core` in part). If a future shadcn release drops the MCP SDK, or moves `shadcn/tailwind.css` into a standalone package so `shadcn` can leave `dependencies` entirely, those six retire together. That is the single highest-value upstream change available to this project's dependency tree.

## Deliberately left open

One Dependabot alert is **not** dismissed and **not** fixed: `@hono/node-server` (#27, moderate 5.9) — path traversal in `serve-static` on Windows via an encoded backslash (`%5C`).

It fails condition 3 and cannot be overridden:

- `@modelcontextprotocol/sdk@1.29.0` pins `@hono/node-server: ^1.19.9`.
- The fix ships only in **2.0.5+** — a major version outside that range.
- The fix was **not backported**. `1.19.17` (the newest 1.x) was installed and audited during triage: the advisory persists.

Overriding to `^2.0.5` would hand the MCP SDK a major version it was never written against, and no check available in this repository would detect a break — nothing under `src/` imports the MCP server, so `build`, `lint`, and all 1014 tests pass identically whether the pairing works or is completely broken. A green run would prove only that the already-unaffected parts are still unaffected.

It is also not applicable: the traversal relies on Windows treating `\` as a path separator (development is macOS), and this project runs no hono server and serves no static files through one.

**Left open rather than dismissed, deliberately.** An open alert is a live tripwire — when shadcn or the MCP SDK moves to `@hono/node-server` 2.x, Dependabot re-scans and closes it automatically. A dismissal would hide it from the default view and signal nothing when the constraint lifts. The cost is one visible moderate alert on the Security tab; the benefit is that the constraint cannot be silently forgotten.

## Alternatives considered

**Leave the advisories open.** Rejected: 21 high-severity alerts on a public repository are visible to anyone who opens the Security tab, and reachability can change with any refactor.

**Remove `shadcn` from `dependencies`.** Attempted, reverted — `src/index.css` imports `shadcn/tailwind.css`. The build fails without it.

**Replace `shadcn`.** Disproportionate. The design system is built on its component conventions (`docs/design-system.md`); replacing it to shed transitive advisories would be a refactor-class change to fix a build-tooling problem.

**Wait for upstream.** Correct for one or two advisories, not for 51 spanning nine packages with no release timeline.
