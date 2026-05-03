export { toIsoDate, formatRangeLabel } from '@/components/DateRangePicker';
import { readDraftFromParams as baseFn } from '@/components/DateRangePicker';
import type { DateRange } from 'react-day-picker';
import { DateRangePicker } from '@/components/DateRangePicker';

export function readDraftFromParams(params: URLSearchParams): DateRange | undefined {
  return baseFn(params, 'from', 'to');
}

export function MovementsDateRangePicker() {
  return <DateRangePicker fromKey="from" toKey="to" />;
}
