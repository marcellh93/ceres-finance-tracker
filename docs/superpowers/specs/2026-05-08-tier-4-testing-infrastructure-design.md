# Tier 4 Testing Infrastructure Design — 2026-05-08

**Roadmap reference:** `docs/roadmap-phase-three.md` § Stage 5 → Tier 4 testing and observability (T4.16–T4.18).

**Status:** Approved 2026-05-08. Implementation pending.

## Summary

Three independent infrastructure improvements, each shipping as its own commit:

| ID | Item | Why now |
|---|---|---|
| T4.16 | Disable CSS animations in `test-setup.ts` | Removes flake risk from skeleton/data cross-fades and motion-token transitions in jsdom |
| T4.17 | Add `vitest-axe` with `MovementForm`, `QuickAddModal`, `AppLayout` coverage | Catches `serious`/`critical` axe violations as failing tests |
| T4.18 | Bundle visualizer + per-asset size budgets | Build-time observability + CI gate against unintended bundle growth |

## Decisions captured during brainstorm

- **T4.17 axe severity gate:** `serious` and `critical` only. Skips `moderate` and `minor` so the suite isn't fragile to color-contrast nits.
- **T4.18 budget approach:** Per-asset budgets on the four largest chunks. Each at current size + 20% headroom. Single total-bundle budget rejected because chunk-level regressions could be masked by shrinkage elsewhere.
- **Order:** T4.16 → T4.17 → T4.18. Smallest first; testing-related work before build-tooling.
- **No new architectural patterns:** Each item is bounded scope; no shared abstractions across them.

---

## T4.16 — Disable CSS animations in test-setup

### Surface

`ProjectCeres.Client/src/test-setup.ts` only.

### Problem

The SPA uses `transition-colors`, `transition-transform`, the `--motion-duration-base` token, and CSS animations (`animate-spin`, skeleton pulse). In jsdom these don't run visually but the `transitionend` and `animationend` events can still fire async, and any test that asserts on a "post-transition" state can race the timer. After the T3.11 motion-token migration, several tests that used `await waitFor(...)` survived only because the default 1000 ms timeout absorbs the 180 ms transition. Disabling animations in tests makes assertions deterministic.

### Implementation

Append a global stylesheet to the JSDOM document head in `test-setup.ts`:

```ts
// Disable CSS animations and transitions in tests so visual state is
// deterministic by the time render() returns. JSDOM does not animate
// pixels, but transitionend/animationend events still fire on a timer
// — zeroing out durations removes that timing variance.
const style = document.createElement('style');
style.textContent = `
  *, *::before, *::after {
    animation-duration: 0s !important;
    animation-delay: 0s !important;
    transition-duration: 0s !important;
    transition-delay: 0s !important;
    scroll-behavior: auto !important;
  }
`;
document.head.appendChild(style);
```

`scroll-behavior: auto` matters for tests that assert against scroll-restoration paths — without this the smooth-scroll could complete after the test assertion runs.

### Out of scope

- Doesn't affect the `useDelayedLoading` hook (that's `setTimeout`-based, not CSS).
- Doesn't affect Sonner's toast auto-dismiss (also timer-based).
- Both are correctly handled by `vi.useFakeTimers()` per-test where needed.

### Tests

No new test file needed. The existing 843 tests should remain green; the change reduces flakiness rather than introducing new behavior.

---

## T4.17 — vitest-axe accessibility tests

### Dependencies to install

```
pnpm add -D vitest-axe axe-core
```

`vitest-axe` is the Vitest port of `jest-axe`; it depends on `axe-core` for the actual rule engine. Both have stable APIs since 2024.

### Per-test setup helper

Create `ProjectCeres.Client/src/app/lib/test-axe.ts` with a thin wrapper that:

- Configures axe to skip rules below `serious` severity.
- Returns a function `expectNoA11yViolations(container)` that runs axe on a rendered container and asserts no `serious` or `critical` violations remain.

```ts
import { axe, type AxeResults } from 'vitest-axe';
import { expect } from 'vitest';

const SEVERITIES = new Set(['serious', 'critical']);

/**
 * Runs axe-core against the rendered container and asserts there are no
 * `serious` or `critical` violations. `moderate` and `minor` violations are
 * not gated on (they're often colour-contrast nits or recommendations);
 * those are surfaced separately by the `web-design-guidelines` audit.
 */
export async function expectNoA11yViolations(container: HTMLElement) {
  const results = (await axe(container)) as AxeResults;
  const blockers = results.violations.filter((v) => SEVERITIES.has(v.impact ?? ''));
  if (blockers.length > 0) {
    const summary = blockers
      .map((v) => `[${v.impact}] ${v.id}: ${v.description}\n  ${v.nodes.map((n) => n.target.join(' ')).join('\n  ')}`)
      .join('\n\n');
    expect.fail(`A11y violations:\n\n${summary}`);
  }
  expect(blockers).toHaveLength(0);
}
```

### Test files

Three new files, each focused on one component:

#### `ProjectCeres.Client/src/app/features/movements/MovementForm.a11y.test.tsx`

Renders `MovementForm` in `mode="create"` for each of the three movement types (Transaction, Transfer, LiabilityPayment), asserts no axe violations on each. Uses the existing test fixtures from `MovementForm.test.tsx` to keep the harness consistent.

#### `ProjectCeres.Client/src/app/components/QuickAddModal.a11y.test.tsx`

Renders `QuickAddModal` open with each of the three tabs active. Asserts no axe violations per tab.

#### `ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx`

Renders `AppLayout` with a mock outlet (e.g., a placeholder `<div>Dashboard</div>`). Asserts no axe violations. Covers the navigation landmarks, sidebar, and TopBar.

### Expected first-run state

Some violations may surface on the first run — that's the point. If a serious/critical violation fires, the test fails and the violation needs fixing before the suite can be green. Likely candidates: missing button names, low-contrast hover states, missing landmarks.

If a real violation surfaces that requires substantial work to fix, the implementer can `expect.soft` or skip with a `// TODO: fix in <ticket>` comment + open a follow-up issue. Goal of T4.17 is the *gate*, not zero-violation perfection on day one.

### Out of scope

- `MovementsLayout`, `Reports*`, `Settings`, every other surface — covered by their own future a11y tests, not T4.17.
- `moderate`/`minor` violations.
- Color-contrast checks across themes (light/dark) — single render only this round.

---

## T4.18 — Bundle visualizer + size budgets

### Dependencies to install

```
pnpm add -D rollup-plugin-visualizer
```

### Vite config change

Wire `rollup-plugin-visualizer` into the build's `rollupOptions.plugins`. Emit a `dist/stats.html` with treemap output. Open it manually with `open dist/stats.html` after `pnpm build`.

```ts
import { visualizer } from 'rollup-plugin-visualizer';

// inside defineConfig.build.rollupOptions
plugins: [
  visualizer({
    filename: 'dist/stats.html',
    template: 'treemap',
    gzipSize: true,
    brotliSize: false,
  }),
],
```

### Size-budget script

Create `ProjectCeres.Client/scripts/check-bundle-size.mjs`. Reads `dist/manifest.json` (Vite emits this with `manifest: true` already set), or globs `dist/assets/*.js`. Compares each asset's gzipped size against a per-asset budget defined in the script.

Budgets pinned at current size + 20% headroom (rounded to a clean number). Current sizes from a clean build on 2026-05-08:

| Asset glob | Current gzip | Budget gzip |
|---|---|---|
| `app-*.js` | 56.11 kB | 67 kB |
| `vendor-react-*.js` | 94.28 kB | 113 kB |
| `vendor-charts-*.js` | 112.35 kB | 135 kB |
| `vendor-ui-*.js` | 13.57 kB | 16 kB |
| `dialog-*.js` | 28.06 kB | 34 kB |
| `amount-format-*.js` | 67.46 kB | 81 kB |
| `designSystem-*.js` | 11.79 kB | 14 kB |

Anything not matching one of those globs is ignored (small chunks like `main`, `rolldown-runtime`, `GoalBudgetBars` are too small to budget meaningfully). The script:

```js
import { globSync, readFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';

const BUDGETS_GZIP_KB = {
  'app-*.js': 67,
  'vendor-react-*.js': 113,
  'vendor-charts-*.js': 135,
  'vendor-ui-*.js': 16,
  'dialog-*.js': 34,
  'amount-format-*.js': 81,
  'designSystem-*.js': 14,
};

const failures = [];
for (const [pattern, budgetKb] of Object.entries(BUDGETS_GZIP_KB)) {
  const matches = globSync(`dist/assets/${pattern}`);
  if (matches.length === 0) {
    failures.push(`No asset matched ${pattern} — check Vite's chunk naming.`);
    continue;
  }
  for (const file of matches) {
    const raw = readFileSync(file);
    const gzipKb = gzipSync(raw).length / 1024;
    if (gzipKb > budgetKb) {
      failures.push(`${file}: ${gzipKb.toFixed(2)} kB > ${budgetKb} kB budget`);
    }
  }
}

if (failures.length > 0) {
  console.error('Bundle size budget exceeded:\n' + failures.map((f) => '  - ' + f).join('\n'));
  process.exit(1);
}
console.log('Bundle size: all chunks within budget.');
```

Uses `node:fs.globSync` (Node 22+) so no glob package install is needed. Confirmed Node 24.14 is in use.

### Wiring

Add a `check-size` script to `package.json`:

```json
"scripts": {
  "check-size": "node scripts/check-bundle-size.mjs",
  "build": "tsc -b && vite build && pnpm check-size"
}
```

`pnpm build` now fails if any chunk exceeds its budget. Local manual builds catch regressions. Future CI integration is automatic.

### Out of scope

- Brotli sizes (gzip is the conventional metric).
- Per-route bundle budgets (Vite chunks aren't structured around routes yet).
- Source-map-explorer-style per-import audits — the visualizer's treemap is enough for the tier.

---

## File structure

| Path | Status |
|---|---|
| `ProjectCeres.Client/src/test-setup.ts` | Modified — append global stylesheet (T4.16) |
| `ProjectCeres.Client/package.json` | Modified — add `vitest-axe`, `axe-core`, `rollup-plugin-visualizer` to devDependencies; add `check-size` script (T4.17, T4.18) |
| `ProjectCeres.Client/src/app/lib/test-axe.ts` | New — `expectNoA11yViolations` helper (T4.17) |
| `ProjectCeres.Client/src/app/features/movements/MovementForm.a11y.test.tsx` | New (T4.17) |
| `ProjectCeres.Client/src/app/components/QuickAddModal.a11y.test.tsx` | New (T4.17) |
| `ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx` | New (T4.17) |
| `ProjectCeres.Client/vite.config.ts` | Modified — wire `rollup-plugin-visualizer` (T4.18) |
| `ProjectCeres.Client/scripts/check-bundle-size.mjs` | New — budget enforcement script (T4.18) |

## Commit plan

Three commits, in order:

1. **T4.16** — `test(infra): disable CSS animations in test setup for deterministic assertions`
2. **T4.17** — `test(a11y): vitest-axe coverage on MovementForm, QuickAddModal, AppLayout`
3. **T4.18** — `build: bundle visualizer + per-asset size budget gate`

After all three land, mark T4.16/T4.17/T4.18 as `[x]` in `docs/roadmap-phase-three.md` Stage 5 Tier 4 list and bump Stage 5's status line to reflect Tier 4 done.
