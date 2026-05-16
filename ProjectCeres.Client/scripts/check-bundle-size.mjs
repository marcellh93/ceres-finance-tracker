#!/usr/bin/env node
import { globSync, readFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';

// Per-asset gzip size budgets in kB. Values are 2026-05-08 baseline + 20%
// headroom rounded to a clean number. Each pattern globs `dist/assets/`.
const BUDGETS_GZIP_KB = {
  'app-*.js': 67,
  'vendor-react-*.js': 113,
  'vendor-charts-*.js': 135,
  // react-hook-form + zod + @hookform/resolvers: 24.84 kB measured 2026-05-16, ~17% headroom.
  'vendor-forms-*.js': 29,
  'vendor-i18n-*.js': 17,
  'vendor-ui-*.js': 16,
  // vendor-primitives: @base-ui/react + @floating-ui/* + @radix-ui/* + scroll-lock
  // helpers. Split out 2026-05-16 to fix the auto-named amount-format chunk budget
  // breach. 58.47 kB measured 2026-05-16, ~11% headroom.
  'vendor-primitives-*.js': 65,
  // vendor-dates: date-fns + @date-fns/tz + react-day-picker. 19.44 kB measured
  // 2026-05-16, ~13% headroom.
  'vendor-dates-*.js': 22,
  // vendor-overlay: sonner + cmdk + next-themes. 24.79 kB measured 2026-05-16,
  // ~13% headroom.
  'vendor-overlay-*.js': 28,
  'dialog-*.js': 34,
  // amount-format chunk: after vendor splits, contains only shared app-level
  // components (shadcn/ui wrappers, amount-format.ts, date-format.ts, etc.).
  // 24.91 kB measured 2026-05-16, well under old 81 kB budget. Tighten to 30 kB.
  'amount-format-*.js': 30,
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
