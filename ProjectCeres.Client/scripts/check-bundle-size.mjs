#!/usr/bin/env node
import { globSync, readFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';

// Per-asset gzip size budgets in kB. Values are 2026-05-08 baseline + 20%
// headroom rounded to a clean number. Each pattern globs `dist/assets/`.
const BUDGETS_GZIP_KB = {
  'app-*.js': 67,
  'vendor-react-*.js': 113,
  'vendor-charts-*.js': 135,
  'vendor-forms-*.js': 29,
  'vendor-i18n-*.js': 17,
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
