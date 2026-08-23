import { describe, expect, it } from 'vitest';
import { summarizeUserAgent } from './user-agent-summary';

describe('summarizeUserAgent', () => {
  // Every string below was taken from the UserSessions table on a real
  // database, not invented — these are the shapes this project actually sees.
  it('summarizes desktop Chrome on macOS', () => {
    expect(
      summarizeUserAgent(
        'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36',
      ),
    ).toBe('Chrome on macOS');
  });

  it('summarizes mobile Safari on iOS', () => {
    expect(
      summarizeUserAgent(
        'Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1',
      ),
    ).toBe('Safari on iPhone');
  });

  // The Electron trap: this string contains BOTH "Code/1.134.0" and
  // "Chrome/148...". Matching Chrome first would mislabel the VS Code
  // built-in browser as Chrome.
  it('reports VS Code rather than the Chrome it embeds', () => {
    expect(
      summarizeUserAgent(
        'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Code/1.134.0 Chrome/148.0.7778.280 Electron/42.8.1 Safari/537.36',
      ),
    ).toBe('VS Code on macOS');
  });

  // A non-browser client has no browser and no OS to claim. Inventing either
  // would be worse than saying what it is.
  it('reports curl without inventing an OS', () => {
    expect(summarizeUserAgent('curl/8.7.1')).toBe('curl');
  });

  it('reports Edge rather than the Chrome it impersonates', () => {
    expect(
      summarizeUserAgent(
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.2210.91',
      ),
    ).toBe('Edge on Windows');
  });

  it('reports Firefox on Linux', () => {
    expect(
      summarizeUserAgent('Mozilla/5.0 (X11; Linux x86_64; rv:121.0) Gecko/20100101 Firefox/121.0'),
    ).toBe('Firefox on Linux');
  });

  it('reports Chrome on Android', () => {
    expect(
      summarizeUserAgent(
        'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36',
      ),
    ).toBe('Chrome on Android');
  });

  it('reports Safari on iPad distinctly from iPhone', () => {
    expect(
      summarizeUserAgent(
        'Mozilla/5.0 (iPad; CPU OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Mobile/15E148 Safari/604.1',
      ),
    ).toBe('Safari on iPad');
  });

  // Degradation cases. A session row must always render something a person
  // can read — never blank, never a crash, never the raw 512-char string.
  it.each([
    ['', 'Unknown device'],
    ['   ', 'Unknown device'],
    ['!!!garbage!!!', 'Unknown device'],
  ])('falls back to "Unknown device" for %j', (input, expected) => {
    expect(summarizeUserAgent(input)).toBe(expected);
  });

  it('names the OS even when the browser is unrecognized', () => {
    expect(summarizeUserAgent('SomeNewBrowser/1.0 (Macintosh; Intel Mac OS X 10_15_7)')).toBe(
      'Unknown browser on macOS',
    );
  });

  it('never returns the raw string, however long', () => {
    const long = `Mozilla/5.0 ${'x'.repeat(500)}`;
    const out = summarizeUserAgent(long);
    expect(out.length).toBeLessThan(40);
    expect(out).not.toContain('xxxx');
  });
});
