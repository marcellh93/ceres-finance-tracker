import { axe } from 'vitest-axe';
import type { AxeResults, Result, NodeResult } from 'axe-core';
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
  const blockers = results.violations.filter((v: Result) => SEVERITIES.has(v.impact ?? ''));
  if (blockers.length > 0) {
    const summary = blockers
      .map((v: Result) => `[${v.impact}] ${v.id}: ${v.description}\n  ${v.nodes.map((n: NodeResult) => n.target.join(' ')).join('\n  ')}`)
      .join('\n\n');
    expect.fail(`A11y violations:\n\n${summary}`);
  }
  expect(blockers).toHaveLength(0);
}
