/**
 * Amount input format helpers — locale-aware (driven by user settings,
 * not browser locale).
 *
 * Two formats are supported, one each for the two NumberFormat values
 * stored in Settings:
 *   - 'period_decimal' → "1,234.56" (US-style)
 *   - 'comma_decimal'  → "1.234,56" (EU-style)
 *
 * Convention used here:
 *   - "raw"     = digits + the user's decimal separator only, no grouping.
 *                 What the field shows while focused. Example: "1234,56".
 *   - "display" = raw with thousands separators inserted. What the field
 *                 shows on blur. Example: "1.234,56".
 *   - "wire"    = JS number, period decimal. What goes over the network.
 *                 Example: 1234.56.
 */

export type NumberFormat = 'comma_decimal' | 'period_decimal';

export type AmountSeparators = {
  decimal: '.' | ',';
  thousand: '.' | ',' | ' ';
};

export function separatorsFor(format: NumberFormat): AmountSeparators {
  return format === 'comma_decimal'
    ? { decimal: ',', thousand: '.' }
    : { decimal: '.', thousand: ',' };
}

/**
 * Filter a keystroke-by-keystroke string down to digits + at most one
 * occurrence of the user's decimal separator. Substitution behavior:
 *   - The user's decimal separator is kept.
 *   - The "wrong" separator (period under comma_decimal, or vice versa)
 *     is substituted for the correct one — the user clearly meant a
 *     decimal separator.
 *   - A second separator (after the first, regardless of which kind)
 *     is dropped.
 *   - Anything else (letters, spaces, currency symbols) is dropped.
 *
 * Use as the `onChange` filter for the Amount field.
 */
export function sanitizeAmountInput(input: string, format: NumberFormat): string {
  const { decimal } = separatorsFor(format);
  const otherSeparator = decimal === ',' ? '.' : ',';

  let result = '';
  let seenDecimal = false;
  for (const ch of input) {
    if (ch >= '0' && ch <= '9') {
      result += ch;
      continue;
    }
    if ((ch === decimal || ch === otherSeparator) && !seenDecimal) {
      // Normalize the wrong separator to the user's chosen one.
      result += decimal;
      seenDecimal = true;
      continue;
    }
    // Otherwise (second separator, letters, spaces, etc.): drop.
  }
  return result;
}

/**
 * Convert raw input to a JS number. Returns NaN if the input is empty or
 * malformed. The caller decides whether NaN is an error or a "no value"
 * signal.
 */
export function parseAmountToNumber(raw: string, format: NumberFormat): number {
  if (!raw) return NaN;
  const { decimal } = separatorsFor(format);
  const normalized = decimal === ',' ? raw.replace(',', '.') : raw;
  return Number(normalized);
}

/** Insert thousands separators into raw input. Pure presentation. */
export function formatAmountForDisplay(raw: string, format: NumberFormat): string {
  if (!raw) return '';
  const { decimal, thousand } = separatorsFor(format);
  const [intPart, fracPart] = raw.split(decimal);
  const intWithGrouping = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, thousand);
  return fracPart !== undefined ? `${intWithGrouping}${decimal}${fracPart}` : intWithGrouping;
}

/** Inverse of formatAmountForDisplay — strip thousands separators. */
export function stripThousandSeparators(display: string, format: NumberFormat): string {
  const { thousand } = separatorsFor(format);
  // Use a regex to remove every occurrence of the thousand separator.
  // Works for ',', '.', ' ' — none of which need escaping for character classes.
  return display.split(thousand).join('');
}

/** Format a JS number for display. Used for prefilling Edit forms. */
export function formatNumberForDisplay(value: number, format: NumberFormat): string {
  if (!Number.isFinite(value)) return '';
  const { decimal } = separatorsFor(format);
  // Use Intl with a known locale that matches the format, then swap if needed.
  // Easier: do it manually so we don't depend on locale availability.
  const fixed = value.toFixed(2); // always "1234.56"
  const [intPart, fracPart] = fixed.split('.');
  return formatAmountForDisplay(`${intPart}${decimal}${fracPart}`, format);
}

/** Placeholder shown in empty Amount fields. */
export function amountPlaceholder(format: NumberFormat): string {
  return format === 'comma_decimal' ? '0,00' : '0.00';
}
