export { toIsoDate, formatRangeLabel } from '@/components/date-range-picker-utils';
import { readDraftFromParams as baseFn } from '@/components/date-range-picker-utils';
import type { DateRange } from 'react-day-picker';

export function readDraftFromParams(params: URLSearchParams): DateRange | undefined {
  return baseFn(params, 'from', 'to');
}
