function parseRgb(input: string): [number, number, number] {
  const m = input.match(/rgba?\(([^)]+)\)/i);
  if (!m) throw new Error(`Cannot parse color: ${input}`);
  const parts = m[1].split(',').map((s) => parseFloat(s.trim()));
  return [parts[0], parts[1], parts[2]];
}

function relativeLuminance([r, g, b]: [number, number, number]): number {
  const channel = (c: number) => {
    const v = c / 255;
    return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/**
 * Compute the WCAG 2.1 contrast ratio between two `rgb(r,g,b)` strings.
 * Use `getComputedStyle(el).backgroundColor` / `.color` to resolve
 * CSS custom properties to concrete rgb() values before passing in.
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
