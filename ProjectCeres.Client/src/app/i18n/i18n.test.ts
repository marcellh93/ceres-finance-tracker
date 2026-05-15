import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import es from './locales/es.json';

function flattenKeys(obj: Record<string, unknown>, prefix = ''): string[] {
  const keys: string[] = [];
  for (const [k, v] of Object.entries(obj)) {
    const path = prefix ? `${prefix}.${k}` : k;
    if (v !== null && typeof v === 'object') {
      keys.push(...flattenKeys(v as Record<string, unknown>, path));
    } else {
      keys.push(path);
    }
  }
  return keys;
}

describe('i18n locale parity', () => {
  it('every key in en.json exists in es.json', () => {
    const enKeys = new Set(flattenKeys(en));
    const esKeys = new Set(flattenKeys(es));
    const missing = [...enKeys].filter((k) => !esKeys.has(k));
    expect(missing, `missing in es.json: ${missing.join(', ')}`).toHaveLength(0);
  });

  it('every key in es.json exists in en.json', () => {
    const enKeys = new Set(flattenKeys(en));
    const esKeys = new Set(flattenKeys(es));
    const extra = [...esKeys].filter((k) => !enKeys.has(k));
    expect(extra, `unexpected in es.json: ${extra.join(', ')}`).toHaveLength(0);
  });

  it('no value is an empty string', () => {
    const allValues = (obj: Record<string, unknown>): unknown[] =>
      Object.values(obj).flatMap((v) =>
        v !== null && typeof v === 'object' ? allValues(v as Record<string, unknown>) : [v]
      );
    expect(allValues(en).every((v) => typeof v === 'string' && v.length > 0)).toBe(true);
    expect(allValues(es).every((v) => typeof v === 'string' && v.length > 0)).toBe(true);
  });
});
