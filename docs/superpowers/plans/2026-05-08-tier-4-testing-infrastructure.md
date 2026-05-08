# Tier 4 Testing Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the three Stage 5 Tier 4 testing-infrastructure items: disable CSS animations in tests (T4.16), add `vitest-axe` coverage on three components (T4.17), and wire a bundle visualizer + per-asset size budget gate (T4.18).

**Architecture:** Three independent commits, low-to-high risk and dependency. T4.16 is a single-file edit to `test-setup.ts`. T4.17 installs `vitest-axe` + `axe-core` and adds three a11y test files plus a shared helper. T4.18 installs `rollup-plugin-visualizer`, edits `vite.config.ts`, and adds a `scripts/check-bundle-size.mjs` budget gate wired into the build script.

**Tech Stack:** Vitest, jsdom, `@testing-library/react`, `vitest-axe` (new), `axe-core` (new), `rollup-plugin-visualizer` (new), Node 24's built-in `node:fs.globSync`. No new runtime dependencies — devDependencies only.

**Spec reference:** `docs/superpowers/specs/2026-05-08-tier-4-testing-infrastructure-design.md` (commit `3beb598`).

---

## File structure

### Files created

| Path | Responsibility |
|---|---|
| `ProjectCeres.Client/src/app/lib/test-axe.ts` | `expectNoA11yViolations(container)` helper — runs axe and asserts no `serious`/`critical` violations. |
| `ProjectCeres.Client/src/app/features/movements/MovementForm.a11y.test.tsx` | Vitest a11y tests for `MovementForm` in all three movement-type modes. |
| `ProjectCeres.Client/src/app/components/QuickAddModal.a11y.test.tsx` | Vitest a11y tests for `QuickAddModal` in all three tabs. |
| `ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx` | Vitest a11y test for `AppLayout` shell. |
| `ProjectCeres.Client/scripts/check-bundle-size.mjs` | Per-asset gzip size-budget enforcement script. |

### Files modified

| Path | Why |
|---|---|
| `ProjectCeres.Client/src/test-setup.ts` | T4.16 — append global stylesheet zeroing out animation/transition durations. |
| `ProjectCeres.Client/package.json` | T4.17 + T4.18 — add `vitest-axe`, `axe-core`, `rollup-plugin-visualizer` to devDependencies; add `check-size` script; chain it into `build`. |
| `ProjectCeres.Client/vite.config.ts` | T4.18 — wire `rollup-plugin-visualizer` into the build's plugins to emit `dist/stats.html`. |

No other files are touched.

---

## Task 1: T4.16 — Disable CSS animations in test-setup

**Files:**
- Modify: `ProjectCeres.Client/src/test-setup.ts`

### Step 1: Edit `test-setup.ts`

- [ ] **Step 1: Append the global stylesheet block**

Open `ProjectCeres.Client/src/test-setup.ts`. Find the closing brace of the file (after the `getBoundingClientRect` polyfill at line ~48). Add the following after the last existing block:

```ts

// Disable CSS animations and transitions in tests so visual state is
// deterministic by the time render() returns. JSDOM does not animate
// pixels, but transitionend/animationend events still fire on a timer
// — zeroing out durations removes that timing variance.
const noMotionStyle = document.createElement('style')
noMotionStyle.textContent = `
  *, *::before, *::after {
    animation-duration: 0s !important;
    animation-delay: 0s !important;
    transition-duration: 0s !important;
    transition-delay: 0s !important;
    scroll-behavior: auto !important;
  }
`
document.head.appendChild(noMotionStyle)
```

### Step 2: Run the full test suite

- [ ] **Step 2: Verify all tests still pass**

Run (foreground per project memory):
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run
```

Expected: 843/843 passing. If a flake hits (`BudgetEdit > discriminator` or `MovementForm > Test 14` per earlier session notes), retry once.

This change should reduce flake risk, not introduce new failures. Any test that was relying on transition timing as a synchronization mechanism will now resolve faster — which is fine because all such tests use `waitFor` or fake timers.

### Step 3: Commit

- [ ] **Step 3: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/test-setup.ts
git commit -m "test(infra): disable CSS animations in test setup for deterministic assertions" -m "Injects a global stylesheet into the JSDOM document head that zeros out animation-duration, animation-delay, transition-duration, transition-delay, and scroll-behavior. JSDOM doesn't animate pixels but transitionend/animationend events still fire on a timer; removing those event timers removes a class of test flakiness that became more relevant after T3.11 migrated transitions to motion tokens. No test logic changes — existing 843 tests stay green and become marginally faster on average."
```

NO `Co-Authored-By:` trailer.

---

## Task 2: T4.17 — vitest-axe accessibility tests

**Files:**
- Modify: `ProjectCeres.Client/package.json`
- Create: `ProjectCeres.Client/src/app/lib/test-axe.ts`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementForm.a11y.test.tsx`
- Create: `ProjectCeres.Client/src/app/components/QuickAddModal.a11y.test.tsx`
- Create: `ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx`

### Step 1: Install dependencies

- [ ] **Step 1: Add `vitest-axe` and `axe-core`**

```bash
cd <repo>/ProjectCeres.Client
pnpm add -D vitest-axe axe-core
```

Expected: `package.json` `devDependencies` gains `vitest-axe` and `axe-core` entries with the resolved versions. `pnpm-lock.yaml` updates.

### Step 2: Create the a11y helper

- [ ] **Step 2: Create `test-axe.ts`**

Create `ProjectCeres.Client/src/app/lib/test-axe.ts`:

```ts
import { axe, type AxeResults } from 'vitest-axe';
import { expect } from 'vitest';

const SEVERITIES = new Set(['serious', 'critical']);

/**
 * Runs axe-core against the rendered container and asserts there are no
 * `serious` or `critical` violations. `moderate` and `minor` violations are
 * not gated on (they're often colour-contrast nits or recommendations);
 * those are surfaced separately by the `web-design-guidelines` audit.
 *
 * Use in component a11y test files alongside existing render() / screen
 * helpers from @testing-library/react.
 */
export async function expectNoA11yViolations(container: HTMLElement): Promise<void> {
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

### Step 3: Create the MovementForm a11y test

- [ ] **Step 3: Create `MovementForm.a11y.test.tsx`**

Create `ProjectCeres.Client/src/app/features/movements/MovementForm.a11y.test.tsx`:

```tsx
import { render } from '@testing-library/react';
import { beforeEach, describe, it, vi } from 'vitest';
import { MovementForm, type MovementFormValues } from './MovementForm';
import { expectNoA11yViolations } from '../../lib/test-axe';

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({
    data: {
      numberFormat: 'period_decimal' as const,
      dateFormat: 'MM/DD/YYYY',
      defaultCurrencyCode: 'USD',
      defaultCurrencySymbol: '$',
    },
    loading: false,
  }),
}));

beforeEach(() => {
  global.fetch = vi.fn(async () => ({
    ok: true,
    status: 200,
    json: async () => [],
  })) as unknown as typeof fetch;
});

const emptyValues: MovementFormValues = {
  date: '2026-04-30',
  amount: '',
  description: '',
  accountId: null,
  categoryId: null,
  sourceAccountId: null,
  destAccountId: null,
  assetAccountId: null,
  liabilityAccountId: null,
  isCleared: false,
  budgetId: null,
  needsReview: false,
};

const accounts = [
  { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
  { id: 'a2', name: 'Credit Card', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Liability' },
];
const categories = [
  { id: 'c1', name: 'Salary', categoryTypeName: 'Income' },
  { id: 'c2', name: 'Groceries', categoryTypeName: 'Expense' },
];

const noopSubmit = vi.fn().mockResolvedValue({ ok: true as const });
const noopCancel = vi.fn();

describe('MovementForm a11y', () => {
  it('Transaction mode has no serious or critical axe violations', async () => {
    const { container } = render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={noopCancel}
      />,
    );
    await expectNoA11yViolations(container);
  });

  it('Transfer mode has no serious or critical axe violations', async () => {
    const { container } = render(
      <MovementForm
        type="Transfer"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={noopCancel}
      />,
    );
    await expectNoA11yViolations(container);
  });

  it('LiabilityPayment mode has no serious or critical axe violations', async () => {
    const { container } = render(
      <MovementForm
        type="LiabilityPayment"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={noopCancel}
      />,
    );
    await expectNoA11yViolations(container);
  });
});
```

### Step 4: Create the QuickAddModal a11y test

- [ ] **Step 4: Create `QuickAddModal.a11y.test.tsx`**

Create `ProjectCeres.Client/src/app/components/QuickAddModal.a11y.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, it, vi } from 'vitest';
import { QuickAddModal } from './QuickAddModal';
import { expectNoA11yViolations } from '../lib/test-axe';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('../lib/use-settings', () => ({
  useSettings: () => ({
    data: {
      numberFormat: 'period_decimal' as const,
      dateFormat: 'MM/DD/YYYY',
      defaultCurrencyCode: 'EUR',
      defaultCurrencySymbol: '€',
    },
    loading: false,
  }),
}));

beforeEach(() => {
  global.fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/api/accounts/active')) {
      return {
        ok: true,
        json: async () => [
          { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
          { id: 'a2', name: 'Savings', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
          { id: 'a3', name: 'Credit Card', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Liability' },
        ],
      } as Response;
    }
    if (url.includes('/api/categories/active')) {
      return {
        ok: true,
        json: async () => [{ id: 'c1', name: 'Groceries', categoryTypeName: 'Expense' }],
      } as Response;
    }
    return { ok: true, status: 201, json: async () => ({ id: 'new-id' }) } as Response;
  }) as unknown as typeof fetch;
});

describe('QuickAddModal a11y', () => {
  it('Transaction tab has no serious or critical axe violations', async () => {
    const { container } = render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    await waitFor(() => expect(screen.getByRole('tab', { name: /transaction/i })).toBeInTheDocument());
    await expectNoA11yViolations(container);
  });

  it('Transfer tab has no serious or critical axe violations', async () => {
    const { container } = render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    await waitFor(() => expect(screen.getByRole('tab', { name: /transfer/i })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('tab', { name: /transfer/i }));
    await expectNoA11yViolations(container);
  });

  it('Debt Payment tab has no serious or critical axe violations', async () => {
    const { container } = render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    await waitFor(() => expect(screen.getByRole('tab', { name: /debt payment/i })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('tab', { name: /debt payment/i }));
    await expectNoA11yViolations(container);
  });
});
```

If a `expect` symbol is not auto-imported by vitest, add `import { expect } from 'vitest';` near the top — the existing `MovementForm.test.tsx` does the same.

### Step 5: Create the AppLayout a11y test

- [ ] **Step 5: Create `AppLayout.a11y.test.tsx`**

Create `ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx`:

```tsx
import { render } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { beforeEach, describe, it, vi } from 'vitest';
import { AppLayout } from './AppLayout';
import { expectNoA11yViolations } from '../lib/test-axe';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

beforeEach(() => {
  // ReminderCountProvider and ReviewCountProvider both fetch on mount.
  // Returning empty/zero responses keeps them happy without surfacing badges.
  global.fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/pending/count')) {
      return { ok: true, json: async () => 0 } as Response;
    }
    return { ok: true, json: async () => [] } as Response;
  }) as unknown as typeof fetch;
});

describe('AppLayout a11y', () => {
  it('renders without serious or critical axe violations', async () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<main><h1>Test page</h1></main>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    await expectNoA11yViolations(container);
  });
});
```

### Step 6: Run the new tests in isolation first

- [ ] **Step 6: Run the three new files**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/features/movements/MovementForm.a11y.test.tsx src/app/components/QuickAddModal.a11y.test.tsx src/app/layout/AppLayout.a11y.test.tsx
```

Expected: green. If a serious/critical violation surfaces:

- **Read the violation message** — it lists the failing rule, the impact level, and the offending DOM node selector.
- **Fix the underlying component** — don't suppress the test. Common quick fixes: missing `aria-label` on icon-only buttons, missing `role="region"` on landmark sections, missing `<label>` on form inputs.
- **If the fix is non-trivial**, mark that single `it()` with `it.skip(...)` and add a `// TODO: T4.17 follow-up — <issue>` comment plus open a follow-up issue. Goal of T4.17 is the gate; perfect-zero on day one is not required if a fix is large.
- **Re-run** until the suite passes.

If no violations, all three test files pass on the first try.

### Step 7: Run the full suite

- [ ] **Step 7: Verify nothing else broke**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run
```

Expected: 843 prior + 7 new (3 MovementForm + 3 QuickAddModal + 1 AppLayout) = 850 passing. Modulo the same intermittent flake.

### Step 8: Run the production build

- [ ] **Step 8: Build**

```bash
cd <repo>/ProjectCeres.Client
pnpm build
```

Expected: clean. `vitest-axe` and `axe-core` are devDependencies — they should not appear in the production bundle. Verify the bundle didn't grow unexpectedly: `vendor-react`, `vendor-ui`, etc. sizes should match the pre-T4.17 numbers. If `app-*.js` grew, axe leaked into the runtime — investigate the import in `test-axe.ts`.

### Step 9: Commit

- [ ] **Step 9: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/package.json ProjectCeres.Client/pnpm-lock.yaml ProjectCeres.Client/src/app/lib/test-axe.ts ProjectCeres.Client/src/app/features/movements/MovementForm.a11y.test.tsx ProjectCeres.Client/src/app/components/QuickAddModal.a11y.test.tsx ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx
git commit -m "test(a11y): vitest-axe coverage on MovementForm, QuickAddModal, AppLayout (T4.17)" -m "Adds vitest-axe + axe-core as devDependencies and a shared expectNoA11yViolations helper at src/app/lib/test-axe.ts that gates on serious + critical severities only (moderate/minor are often contrast nits already covered by web-design-guidelines audits). Three new test files: MovementForm in all three movement-type modes, QuickAddModal in all three tabs, AppLayout shell. Goal of this round is the gate, not zero violations across every surface — other components get their own a11y tests in future tiers."
```

NO `Co-Authored-By:` trailer.

---

## Task 3: T4.18 — Bundle visualizer + size budgets

**Files:**
- Modify: `ProjectCeres.Client/package.json`
- Modify: `ProjectCeres.Client/vite.config.ts`
- Create: `ProjectCeres.Client/scripts/check-bundle-size.mjs`

### Step 1: Install rollup-plugin-visualizer

- [ ] **Step 1: Add the dependency**

```bash
cd <repo>/ProjectCeres.Client
pnpm add -D rollup-plugin-visualizer
```

Expected: `package.json` `devDependencies` gains `rollup-plugin-visualizer`.

### Step 2: Wire the visualizer into vite.config.ts

- [ ] **Step 2: Edit `vite.config.ts`**

Open `ProjectCeres.Client/vite.config.ts`. Add the import at the top:

```ts
import { visualizer } from 'rollup-plugin-visualizer';
```

In the `build.rollupOptions` block, find the `output: { manualChunks(id) { ... } }` section and add a `plugins` array as a sibling. The new top of the rollupOptions should look like:

```ts
    rollupOptions: {
      input: {
        main: path.resolve(__dirname, 'index.html'),
        designSystem: path.resolve(__dirname, 'design-system.html'),
        app: path.resolve(__dirname, 'app.html'),
      },
      plugins: [
        visualizer({
          filename: 'dist/stats.html',
          template: 'treemap',
          gzipSize: true,
          brotliSize: false,
        }),
      ],
      output: {
        manualChunks(id) {
          if (id.includes('node_modules/react') || id.includes('node_modules/react-dom')) return 'vendor-react'
          if (id.includes('node_modules/recharts'))    return 'vendor-charts'
          if (id.includes('node_modules/lucide-react') || id.includes('node_modules/clsx') ||
              id.includes('node_modules/tailwind-merge') || id.includes('node_modules/class-variance-authority'))
            return 'vendor-ui'
        },
      },
    },
```

Don't move existing lines around — just add the import and the `plugins` array.

### Step 3: Verify the visualizer emits stats.html

- [ ] **Step 3: Run a build and check `dist/stats.html` is created**

```bash
cd <repo>/ProjectCeres.Client
pnpm build
ls -lh dist/stats.html
```

Expected: `dist/stats.html` exists, ~few hundred kB. Open with `open dist/stats.html` if you want to verify the treemap renders, but tooling-wise the file's existence is enough.

### Step 4: Create the size-budget script

- [ ] **Step 4: Create `check-bundle-size.mjs`**

Create the directory if it doesn't exist:

```bash
mkdir -p <repo>/ProjectCeres.Client/scripts
```

Create `ProjectCeres.Client/scripts/check-bundle-size.mjs`:

```js
#!/usr/bin/env node
import { globSync, readFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';

// Per-asset gzip size budgets in kB. Values are 2026-05-08 baseline + 20%
// headroom rounded to a clean number. Each pattern globs `dist/assets/`.
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
const reports = [];
for (const [pattern, budgetKb] of Object.entries(BUDGETS_GZIP_KB)) {
  const matches = globSync(`dist/assets/${pattern}`);
  if (matches.length === 0) {
    failures.push(`No asset matched dist/assets/${pattern} — check Vite's chunk naming.`);
    continue;
  }
  for (const file of matches) {
    const raw = readFileSync(file);
    const gzipKb = gzipSync(raw).length / 1024;
    const ok = gzipKb <= budgetKb;
    reports.push(`  ${ok ? '✓' : '✗'} ${file}: ${gzipKb.toFixed(2)} kB (budget ${budgetKb} kB)`);
    if (!ok) {
      failures.push(`${file}: ${gzipKb.toFixed(2)} kB > ${budgetKb} kB budget`);
    }
  }
}

console.log('Bundle size check:');
console.log(reports.join('\n'));

if (failures.length > 0) {
  console.error('\nBundle size budget exceeded:');
  for (const f of failures) console.error('  - ' + f);
  process.exit(1);
}
console.log('\nAll chunks within budget.');
```

### Step 5: Wire the script into package.json

- [ ] **Step 5: Add the script and chain it into build**

Open `ProjectCeres.Client/package.json`. Find the existing `scripts` block:

```json
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "lint": "eslint .",
    "preview": "vite preview",
    "test": "vitest run",
    "test:watch": "vitest"
  },
```

Replace with:

```json
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build && pnpm check-size",
    "lint": "eslint .",
    "preview": "vite preview",
    "test": "vitest run",
    "test:watch": "vitest",
    "check-size": "node scripts/check-bundle-size.mjs"
  },
```

### Step 6: Verify the script runs and passes

- [ ] **Step 6: Run a build with the gate**

```bash
cd <repo>/ProjectCeres.Client
pnpm build
```

Expected output (last lines):
```
Bundle size check:
  ✓ dist/assets/app-XXXXXXXX.js: <56 kB>
  ✓ dist/assets/vendor-react-XXXXXXXX.js: <94 kB>
  ✓ dist/assets/vendor-charts-XXXXXXXX.js: <112 kB>
  ✓ dist/assets/vendor-ui-XXXXXXXX.js: <13 kB>
  ✓ dist/assets/dialog-XXXXXXXX.js: <28 kB>
  ✓ dist/assets/amount-format-XXXXXXXX.js: <67 kB>
  ✓ dist/assets/designSystem-XXXXXXXX.js: <11 kB>

All chunks within budget.
```

Exact sizes will match (or be slightly below) the spec's baseline values.

If a budget is exceeded with the unmodified bundle, the current spec values are wrong — adjust the budget in `check-bundle-size.mjs` to (current size + 20%) and document why.

If a "No asset matched" failure surfaces, Vite changed its chunk naming since the spec was written — inspect `dist/assets/` and update the glob pattern.

### Step 7: Verify the script fails when budget is exceeded

- [ ] **Step 7: Sanity check — temporarily lower a budget**

To prove the gate is functional, temporarily edit `check-bundle-size.mjs` and lower one budget below current size (e.g. `'app-*.js': 1`). Run:

```bash
cd <repo>/ProjectCeres.Client
pnpm check-size
echo "exit: $?"
```

Expected: prints the failure message, `exit: 1`. Restore the original value when done. Do NOT commit the lowered budget.

### Step 8: Commit

- [ ] **Step 8: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/package.json ProjectCeres.Client/pnpm-lock.yaml ProjectCeres.Client/vite.config.ts ProjectCeres.Client/scripts/check-bundle-size.mjs
git commit -m "build: bundle visualizer + per-asset size budget gate (T4.18)" -m "rollup-plugin-visualizer added to the build pipeline so each \`pnpm build\` emits a treemap at \`dist/stats.html\` for manual inspection. A new scripts/check-bundle-size.mjs enforces per-asset gzip budgets on the seven largest chunks (app, vendor-react, vendor-charts, vendor-ui, dialog, amount-format, designSystem) — current size + 20% headroom each. The check runs as a post-build step (\`pnpm build\` chains it via \`&& pnpm check-size\`) so any future bundle regression that pushes a chunk past its budget will fail the build locally and in CI."
```

NO `Co-Authored-By:` trailer.

---

## Task 4: Roadmap update

After all three commits land.

### Step 1: Update `docs/roadmap-phase-three.md`

- [ ] **Step 1: Mark Tier 4 items done**

Open `docs/roadmap-phase-three.md`. Find the Tier 4 list (around line 325):

```markdown
Tier 4 — testing and observability:

- [ ] T4.16 Disable CSS animations in `test-setup.ts` (12.1) — animations not currently disabled in tests
- [ ] T4.17 Add `vitest-axe` (4.6, 12.5) — minimum coverage: `MovementForm`, `QuickAddModal`, `AppLayout`
- [ ] T4.18 Add bundle visualizer (`rollup-plugin-visualizer`) + size budget on `dist/assets/*.js` (5.5)
```

Replace with (substituting the actual commit hashes after each commit lands):

```markdown
Tier 4 — testing and observability: ✅ all shipped 2026-05-08

- [x] T4.16 Disable CSS animations in `test-setup.ts` (12.1) — shipped 2026-05-08 (commit `<task-1-sha>`)
- [x] T4.17 Add `vitest-axe` (4.6, 12.5) — shipped 2026-05-08 (commit `<task-2-sha>`); coverage on MovementForm (3 modes), QuickAddModal (3 tabs), AppLayout shell; gates on `serious`/`critical` severities
- [x] T4.18 Add bundle visualizer (`rollup-plugin-visualizer`) + size budget on `dist/assets/*.js` (5.5) — shipped 2026-05-08 (commit `<task-3-sha>`); per-asset gzip budgets at current+20% headroom on the seven largest chunks
```

Also bump the Stage 5 status line at the top (around line 250):

```markdown
**Status: ⚠️ In progress.** Data-loading ease-in fully rolled out (5.2/5.3/5.4). Tier 1 polish done (T1.1–T1.4 + T1.6 shipped; T1.5 deliberately not shipped). Tier 2 polish done (T2.7–T2.10). Tier 3 polish done (T3.11–T3.15 shipped 2026-05-08). Tier 4–5 pending. Plus a batch of ad-hoc UX fixes shipped on top of the planned tiers — see § Ad-hoc UX fixes below.
```

becomes:

```markdown
**Status: ⚠️ In progress.** Data-loading ease-in fully rolled out (5.2/5.3/5.4). Tier 1 polish done (T1.1–T1.4 + T1.6 shipped; T1.5 deliberately not shipped). Tier 2 polish done (T2.7–T2.10). Tier 3 polish done (T3.11–T3.15 shipped 2026-05-08). Tier 4 testing-infrastructure done (T4.16–T4.18 shipped 2026-05-08). Tier 5 pending. Plus a batch of ad-hoc UX fixes shipped on top of the planned tiers — see § Ad-hoc UX fixes below.
```

Update the sub-stages table at line ~267:

```markdown
| 5.8 | Polish checklist Tier 4 — testing and observability | ❌ Pending | (above) |
```

becomes:

```markdown
| 5.8 | Polish checklist Tier 4 — testing and observability | ✅ 2026-05-08 (T4.16 `<task-1-sha>`, T4.17 `<task-2-sha>`, T4.18 `<task-3-sha>`) | (above) |
```

### Step 2: Update `docs/ceres-polish-checklist-frontend.md`

- [ ] **Step 2: Mark items 16–18 done**

Open `docs/ceres-polish-checklist-frontend.md`. Find the Tier 4 block (around line 224):

```markdown
**Tier 4 — testing and observability:**

16. Disable CSS animations in `test-setup.ts` (12.1)
17. Add `vitest-axe` (4.6, 12.5)
18. Add bundle visualizer + size limit to CI (5.5)
```

Replace with:

```markdown
**Tier 4 — testing and observability:** ✅ all shipped 2026-05-08

16. ✅ Disabled CSS animations in `test-setup.ts` (commit `<task-1-sha>`)
17. ✅ `vitest-axe` with `expectNoA11yViolations` helper, gating on serious + critical severities; coverage on MovementForm, QuickAddModal, AppLayout (commit `<task-2-sha>`)
18. ✅ Bundle visualizer (`rollup-plugin-visualizer`) emits `dist/stats.html` on every build; per-asset gzip budgets enforced via `pnpm check-size` chained into `pnpm build` (commit `<task-3-sha>`)
```

### Step 3: Commit the docs update

- [ ] **Step 3: Commit**

```bash
cd <repo>
git add docs/roadmap-phase-three.md docs/ceres-polish-checklist-frontend.md
git commit -m "docs(roadmap): mark Tier 4 testing infrastructure (T4.16–T4.18) shipped"
```

NO `Co-Authored-By:` trailer.

---

## Self-review notes (for the engineer)

- **Spec coverage:**
  - § T4.16 → Task 1
  - § T4.17 → Task 2 (helper + 3 test files + dependency install)
  - § T4.18 → Task 3 (visualizer + script + package.json wiring)
  - § File structure → matches Tasks 1–3 exactly
  - § Commit plan → Tasks 1–3 commits, plus Task 4 roadmap update
- **Order rationale:** Smallest → medium → build-tooling. T4.16 is a 1-file edit. T4.17 introduces axe coverage but doesn't change build tooling. T4.18 changes the build-time pipeline. Each is independently revertable.
- **Out-of-scope reminders:** No moderate/minor axe gating. No total-bundle budget. No brotli sizing. No per-route bundle budgets. No CI integration (just local-build gate; CI inherits via `pnpm build`).
- **Stay-on-main:** Per project memory, no branches or worktrees. Commit straight to `main`.
- **No `Co-Authored-By` trailer:** Per project memory, omit it from every commit message in this plan.
- **No `git push` suggestions:** This repo has no remote.
- **Foreground tests:** Per project memory, `pnpm test` and `pnpm build` run in the foreground — backgrounding produces empty output for pnpm.
