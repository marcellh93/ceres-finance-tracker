/**
 * Turns a raw User-Agent header into something a person can read in a
 * session list, e.g. "Chrome on macOS".
 *
 * Deliberately small: this identifies a session for a human deciding whether
 * to revoke it, not for feature detection. It never returns the raw string —
 * stored values run to 512 characters and would wreck the table layout.
 */

type Rule = { pattern: RegExp; label: string };

// Order matters. Browsers impersonate each other in the User-Agent string:
// Edge and VS Code both carry "Chrome/", and Chrome carries "Safari/". The
// most specific claim has to win, so the impersonators are listed first.
const BROWSERS: Rule[] = [
  { pattern: /\bEdg(?:e|A|iOS)?\//, label: 'Edge' },
  { pattern: /\bCode\/\d/, label: 'VS Code' },
  { pattern: /\bElectron\//, label: 'Electron app' },
  { pattern: /\bOPR\/|\bOpera\//, label: 'Opera' },
  { pattern: /\bFirefox\/|\bFxiOS\//, label: 'Firefox' },
  { pattern: /\bCriOS\//, label: 'Chrome' },
  { pattern: /\bChrome\//, label: 'Chrome' },
  { pattern: /\bSafari\//, label: 'Safari' },
];

const OSES: Rule[] = [
  { pattern: /\biPhone\b/, label: 'iPhone' },
  { pattern: /\biPad\b/, label: 'iPad' },
  { pattern: /\bAndroid\b/, label: 'Android' },
  { pattern: /\bWindows NT\b|\bWindows\b/, label: 'Windows' },
  { pattern: /\bMac OS X\b|\bMacintosh\b/, label: 'macOS' },
  { pattern: /\bCrOS\b/, label: 'ChromeOS' },
  { pattern: /\bLinux\b|\bX11\b/, label: 'Linux' },
];

// Non-browser clients that carry no OS. Naming the tool is more honest than
// guessing a platform it never claimed.
const TOOLS: Rule[] = [
  { pattern: /^curl\//i, label: 'curl' },
  { pattern: /^wget\//i, label: 'Wget' },
  { pattern: /^PostmanRuntime\//i, label: 'Postman' },
  { pattern: /^python-requests\//i, label: 'Python script' },
  { pattern: /^HTTPie\//i, label: 'HTTPie' },
];

function firstMatch(rules: Rule[], ua: string): string | null {
  for (const { pattern, label } of rules) {
    if (pattern.test(ua)) return label;
  }
  return null;
}

export function summarizeUserAgent(userAgent: string | null | undefined): string {
  const ua = (userAgent ?? '').trim();
  if (ua === '') return 'Unknown device';

  const tool = firstMatch(TOOLS, ua);
  if (tool !== null) return tool;

  const browser = firstMatch(BROWSERS, ua);
  const os = firstMatch(OSES, ua);

  if (browser !== null && os !== null) return `${browser} on ${os}`;
  if (browser !== null) return browser;
  if (os !== null) return `Unknown browser on ${os}`;
  return 'Unknown device';
}
