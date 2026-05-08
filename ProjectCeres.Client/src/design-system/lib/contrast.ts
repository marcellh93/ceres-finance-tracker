/**
 * Parse a CSS color string into an RGB triple in the [0, 255] range.
 *
 * Accepts:
 * - `rgb(r, g, b)` / `rgba(r, g, b, a)` — alpha is dropped
 * - `oklch(L C h)` / `oklch(L C h / α)` — converted via the standard
 *   linear-RGB matrix; alpha is dropped
 *
 * Throws if the input matches neither format.
 */
function parseRgb(input: string): [number, number, number] {
  const trimmed = input.trim();
  const oklchMatch = trimmed.match(/^oklch\(([^)]+)\)$/i);
  if (oklchMatch) {
    return oklchToSrgb(oklchMatch[1]);
  }
  const rgbMatch = trimmed.match(/^rgba?\(([^)]+)\)$/i);
  if (rgbMatch) {
    const parts = rgbMatch[1]
      .split(/[\s,/]+/)
      .map((s) => parseFloat(s.trim()))
      .filter((n) => !Number.isNaN(n));
    return [parts[0], parts[1], parts[2]];
  }
  throw new Error(`Cannot parse color: ${input}`);
}

/**
 * Convert OKLCH components (as a single string like "0.520 0.110 195" or
 * "0.520 0.110 195 / 0.8") to an sRGB triple in [0, 255]. Alpha is dropped.
 *
 * Matrix and gamma constants come from Björn Ottosson's reference OKLab→sRGB
 * implementation. Same constants used in the T3.15 theme-color computation.
 */
function oklchToSrgb(args: string): [number, number, number] {
  const tokens = args.split('/')[0].trim().split(/\s+/);
  const L = parseFloat(tokens[0]);
  const C = parseFloat(tokens[1]);
  const h = parseFloat(tokens[2]);
  if (Number.isNaN(L) || Number.isNaN(C) || Number.isNaN(h)) {
    throw new Error(`Cannot parse OKLCH components: ${args}`);
  }
  const a = C * Math.cos((h * Math.PI) / 180);
  const b = C * Math.sin((h * Math.PI) / 180);
  // OKLab → linear LMS
  const l_ = L + 0.3963377774 * a + 0.2158037573 * b;
  const m_ = L - 0.1055613458 * a - 0.0638541728 * b;
  const s_ = L - 0.0894841775 * a - 1.2914855480 * b;
  const lLms = l_ ** 3;
  const mLms = m_ ** 3;
  const sLms = s_ ** 3;
  // Linear LMS → linear sRGB
  const lr =  4.0767416621 * lLms - 3.3077115913 * mLms + 0.2309699292 * sLms;
  const lg = -1.2684380046 * lLms + 2.6097574011 * mLms - 0.3413193965 * sLms;
  const lb = -0.0041960863 * lLms - 0.7034186147 * mLms + 1.7076147010 * sLms;
  // Linear sRGB → gamma-encoded sRGB → 0–255
  const gamma = (u: number) => (u >= 0.0031308 ? 1.055 * Math.pow(u, 1 / 2.4) - 0.055 : 12.92 * u);
  const clamp = (v: number) => Math.max(0, Math.min(255, Math.round(gamma(v) * 255)));
  return [clamp(lr), clamp(lg), clamp(lb)];
}

function relativeLuminance([r, g, b]: [number, number, number]): number {
  const channel = (c: number) => {
    const v = c / 255;
    return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/**
 * Compute the WCAG 2.1 contrast ratio between two color strings.
 * Use `getComputedStyle(el).backgroundColor` / `.color` to resolve
 * CSS custom properties to concrete color() values before passing in.
 */
export function contrastRatio(a: string, b: string): number {
  const la = relativeLuminance(parseRgb(a));
  const lb = relativeLuminance(parseRgb(b));
  const [light, dark] = la > lb ? [la, lb] : [lb, la];
  return (light + 0.05) / (dark + 0.05);
}

export function formatRatio(ratio: number): string {
  const r = ratio.toFixed(2);
  if (ratio >= 7) return `${r} : 1 (AAA)`;
  if (ratio >= 4.5) return `${r} : 1 (AA)`;
  if (ratio >= 3) return `${r} : 1 (Large only)`;
  return `${r} : 1 (Fail)`;
}
