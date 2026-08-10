# ADR-0079 — pnpm overrides as the remediation path for transitive npm advisories

> **Diataxis type:** Explanation — architectural decision record.

**Status:** Accepted — 2026-08-10. Twelve overrides live in `ProjectCeres.Client/package.json`, one in `ProjectCeres/package.json`. One advisory is deliberately left unfixed — see *Deliberately left open* below.

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
| `hono` | `^4.12.34` | 4.13.1 | 16 | shadcn → @modelcontextprotocol/sdk |
| `undici` | `^7.29.0` | 7.29.0 | 12 | jsdom (test DOM environment) |
| `postcss` | `^8.5.23` | 8.5.26 | 4 | shadcn; vite |
| `brace-expansion` | `^5.0.7` | 5.0.9 | 3 | eslint → minimatch |
| `ip-address` | `^10.3.1` | 10.4.0 | 3 | shadcn → @modelcontextprotocol/sdk → express-rate-limit |
| `js-yaml` | `^4.3.1` | 4.3.1 | 3 | shadcn → cosmiconfig |
| `fast-uri` | `^3.1.5` | 3.1.5 | 3 | shadcn → @modelcontextprotocol/sdk → ajv |
| `nanoid` | `^3.3.18` | 3.3.18 | 2 | postcss (under both shadcn and vite) |
| `qs` | `^6.15.2` | 6.15.3 | 1 | shadcn → @modelcontextprotocol/sdk → express → body-parser |
| `body-parser` | `^2.3.0` | 2.3.0 | 1 | shadcn → @modelcontextprotocol/sdk → express |
| `@babel/core` | `^7.29.6` | 7.29.7 | 1 | shadcn → @babel/preset-typescript; eslint-plugin-react-hooks |
| `esbuild` | `^0.28.1` | 0.28.2 | 1 | vite; tsx |

### `ProjectCeres/package.json`

| Override | Pinned | Resolves to | Cleared | Reached the tree via |
| --- | --- | --- | --- | --- |
| `postcss` | `^8.5.23` | 8.5.26 | (shared) | tailwindcss 3.4.19 |

The Razor-layer entry exists because the same postcss advisory affected both lockfiles. Fixing only the manifest Dependabot named would have left the client on a vulnerable copy.

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
