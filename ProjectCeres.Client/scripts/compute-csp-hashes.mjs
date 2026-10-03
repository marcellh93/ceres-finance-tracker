// Computes sha256 CSP hashes of every inline <script> in the built app.html
// and writes them to csp-hashes.json next to it. Run AFTER vite build.
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const distDir = resolve(import.meta.dirname, '../dist');
const html = readFileSync(resolve(distDir, 'app.html'), 'utf8');

// Match inline <script> blocks only (no src attribute).
const hashes = [];
const re = /<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/g;
let m;
while ((m = re.exec(html)) !== null) {
  const body = m[1];
  const digest = createHash('sha256').update(body, 'utf8').digest('base64');
  hashes.push(`sha256-${digest}`);
}

writeFileSync(resolve(distDir, 'csp-hashes.json'), JSON.stringify(hashes, null, 2));
console.log(`compute-csp-hashes: wrote ${hashes.length} hash(es) to dist/csp-hashes.json`);
