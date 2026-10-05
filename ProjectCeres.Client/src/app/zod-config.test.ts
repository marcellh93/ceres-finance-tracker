import { describe, it, expect } from 'vitest';

// Regression guard for the CSP eval-probe fix (Stage 14). The fix lives in
// index.html as an inline script that sets `globalThis.__zod_globalConfig =
// { jitless: true }` BEFORE any module loads — the only hook immune to bundler
// module-ordering (a `config()` call in app code runs too late; the probe fires
// during schema-module evaluation first). This test pins the contract the inline
// script depends on: that Zod reads __zod_globalConfig from globalThis on import
// and exposes it as its live globalConfig. If a Zod upgrade dropped that hook,
// the inline-script fix would silently stop working and the CSP violations would
// return — this test fails first.
describe('zod jitless pre-config hook (index.html inline script contract)', () => {
  it('Zod adopts globalThis.__zod_globalConfig set before import', async () => {
    (globalThis as { __zod_globalConfig?: { jitless?: boolean } }).__zod_globalConfig = {
      jitless: true,
    };
    const { core } = await import('zod');
    expect(core.globalConfig.jitless).toBe(true);
  });
});
