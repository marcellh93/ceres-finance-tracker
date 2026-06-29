import { useEffect, useState } from 'react';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
  InputGroupText,
} from '@/components/ui/input-group';
import { Skeleton } from '@/components/ui/skeleton';
import {
  amountPlaceholder,
  formatAmountForDisplay,
  formatNumberForDisplay,
  parseAmountToNumber,
  sanitizeAmountInput,
  stripThousandSeparators,
} from '../lib/amount-format';
import { useSettings } from '../lib/use-settings';

type MoneyInputProps = {
  /** DOM id; required so an outer `<label htmlFor>` resolves and stays valid
   *  while the input renders (or while the loading Skeleton is shown). */
  id: string;
  /** Wire format ("123.45" — JS-number-parsable). Empty string means "no value". */
  value: string;
  onChange: (wire: string) => void;
  /** Optional currency symbol shown as a prefix addon. Falsy → no prefix. */
  currencySymbol?: string | null;
  /** Force the loading Skeleton (e.g. when an external dependency such as the
   *  selected account → currency symbol is still resolving). */
  showSkeleton?: boolean;
};

/**
 * Locale-aware money input with display/wire separation:
 * - Reads the user's `numberFormat` from Settings.
 * - On focus: strips thousands separators so editing is unencumbered.
 * - On blur: re-applies thousands separators.
 * - `onChange` is always called with the wire (period-decimal) representation.
 *
 * Renders a Skeleton while `numberFormat` is unresolved or `showSkeleton` is
 * true. The Skeleton carries the same `id` so an outer `<label htmlFor>` stays
 * valid during the loading window.
 */
export function MoneyInput({
  id,
  value,
  onChange,
  currencySymbol,
  showSkeleton = false,
}: MoneyInputProps) {
  const { data: settings } = useSettings();
  const numberFormat = settings?.numberFormat;

  const [displayAmount, setDisplayAmount] = useState('');
  const [focused, setFocused] = useState(false);

  // Sync display from wire when not actively editing. Fires on initial load
  // (Edit page populating the form), on numberFormat change, and after the
  // parent confirms a programmatic update.
  useEffect(() => {
    if (!numberFormat) return;
    if (focused) return;
    if (!value) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- Why: syncing display representation from external wire-format prop; this effect is the canonical "derive display from props" pattern for locale-aware inputs.
      setDisplayAmount('');
      return;
    }
    const asNumber = Number(value);
    setDisplayAmount(
      Number.isFinite(asNumber) ? formatNumberForDisplay(asNumber, numberFormat) : '',
    );
  }, [value, numberFormat, focused]);

  if (numberFormat === undefined || showSkeleton) {
    return <Skeleton id={id} className="h-8 w-full" />;
  }

  function handleChange(input: string) {
    const sanitized = sanitizeAmountInput(input, numberFormat!);
    setDisplayAmount(sanitized);
    const parsed = parseAmountToNumber(sanitized, numberFormat!);
    onChange(Number.isFinite(parsed) ? String(parsed) : '');
  }

  function handleFocus() {
    setFocused(true);
    setDisplayAmount(stripThousandSeparators(displayAmount, numberFormat!));
  }

  function handleBlur() {
    setFocused(false);
    setDisplayAmount(formatAmountForDisplay(displayAmount, numberFormat!));
  }

  return (
    <InputGroup>
      {currencySymbol && (
        <InputGroupAddon align="inline-start">
          <InputGroupText className="text-base font-medium text-muted-foreground">
            {currencySymbol}
          </InputGroupText>
        </InputGroupAddon>
      )}
      <InputGroupInput
        id={id}
        type="text"
        inputMode="decimal"
        autoComplete="off"
        placeholder={amountPlaceholder(numberFormat)}
        value={displayAmount}
        onChange={(e) => handleChange(e.target.value)}
        onFocus={handleFocus}
        onBlur={handleBlur}
        className="text-base font-medium"
      />
    </InputGroup>
  );
}
