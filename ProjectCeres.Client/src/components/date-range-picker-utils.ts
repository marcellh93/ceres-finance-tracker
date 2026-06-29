import type { DateRange } from 'react-day-picker';
import { formatDate } from '../app/lib/date-format';

// ---------- helpers ----------

export function toIsoDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function parseIsoDate(value: string | null): Date | undefined {
  if (!value) return undefined;
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return undefined;
  const d = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  return Number.isNaN(d.getTime()) ? undefined : d;
}

export function readDraftFromParams(
  params: URLSearchParams,
  fromKey: string,
  toKey: string,
): DateRange | undefined {
  const from = parseIsoDate(params.get(fromKey));
  const to = parseIsoDate(params.get(toKey));
  if (!from && !to) return undefined;
  return { from, to };
}

export function formatRangeLabel(
  range: DateRange | undefined,
  dateFormat: string | undefined,
): string {
  if (!range || (!range.from && !range.to)) return 'Any date';
  if (range.from && range.to)
    return `${formatDate(toIsoDate(range.from), dateFormat)} – ${formatDate(toIsoDate(range.to), dateFormat)}`;
  if (range.from) return `From ${formatDate(toIsoDate(range.from), dateFormat)}`;
  return `Until ${formatDate(toIsoDate(range.to as Date), dateFormat)}`;
}

// ---------- preset builders ----------

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

function addDays(d: Date, days: number): Date {
  const next = new Date(d);
  next.setDate(next.getDate() + days);
  return next;
}

type Preset = { label: string; build: () => DateRange | undefined };

export const DATE_RANGE_PRESETS: Preset[] = [
  { label: 'Today', build: () => { const t = startOfDay(new Date()); return { from: t, to: t }; } },
  { label: 'Yesterday', build: () => { const y = addDays(startOfDay(new Date()), -1); return { from: y, to: y }; } },
  {
    label: 'This week',
    build: () => {
      const today = startOfDay(new Date());
      const from = addDays(today, -((today.getDay() + 6) % 7));
      return { from, to: addDays(from, 6) };
    },
  },
  {
    label: 'Last week',
    build: () => {
      const today = startOfDay(new Date());
      const thisMon = addDays(today, -((today.getDay() + 6) % 7));
      const from = addDays(thisMon, -7);
      return { from, to: addDays(from, 6) };
    },
  },
  {
    label: 'This month',
    build: () => {
      const now = new Date();
      return { from: new Date(now.getFullYear(), now.getMonth(), 1), to: new Date(now.getFullYear(), now.getMonth() + 1, 0) };
    },
  },
  {
    label: 'Last month',
    build: () => {
      const now = new Date();
      return { from: new Date(now.getFullYear(), now.getMonth() - 1, 1), to: new Date(now.getFullYear(), now.getMonth(), 0) };
    },
  },
  {
    label: 'This quarter',
    build: () => {
      const now = new Date();
      const qStart = Math.floor(now.getMonth() / 3) * 3;
      return { from: new Date(now.getFullYear(), qStart, 1), to: new Date(now.getFullYear(), qStart + 3, 0) };
    },
  },
  {
    label: 'Year to date',
    build: () => { const now = startOfDay(new Date()); return { from: new Date(now.getFullYear(), 0, 1), to: now }; },
  },
  {
    label: 'Last 12 months',
    build: () => {
      const to = startOfDay(new Date());
      const from = new Date(to);
      from.setFullYear(from.getFullYear() - 1);
      from.setDate(from.getDate() + 1);
      return { from, to };
    },
  },
  { label: 'All time', build: () => undefined },
];
