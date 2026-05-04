import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { DateRange } from 'react-day-picker';
import { CalendarRange } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Calendar } from '@/components/ui/calendar';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { useSettings } from '../app/lib/use-settings';
import { formatDate } from '../app/lib/date-format';

// ---------- helpers (exported for tests / wrapper modules) ----------

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

// ---------- component ----------

type Props = {
  /** URL param key for the start date. Default: 'from' */
  fromKey?: string;
  /** URL param key for the end date. Default: 'to' */
  toKey?: string;
  /** Extra classes applied to the trigger button. Use to control width from the parent. */
  className?: string;
};

export function DateRangePicker({ fromKey = 'from', toKey = 'to', className }: Props) {
  const [params, setParams] = useSearchParams();
  const { data: settings } = useSettings();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<DateRange | undefined>(() =>
    readDraftFromParams(params, fromKey, toKey),
  );

  const fromParam = params.get(fromKey);
  const toParam = params.get(toKey);
  useEffect(() => {
    setDraft(readDraftFromParams(params, fromKey, toKey));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fromParam, toParam]);

  const currentRange = readDraftFromParams(params, fromKey, toKey);
  const triggerLabel = formatRangeLabel(currentRange, settings?.dateFormat);

  function applyRange(range: DateRange | undefined) {
    const next = new URLSearchParams(params);
    if (range?.from) next.set(fromKey, toIsoDate(range.from));
    else next.delete(fromKey);
    if (range?.to) next.set(toKey, toIsoDate(range.to));
    else next.delete(toKey);
    next.delete('page');
    setParams(next, { replace: true });
  }

  function handlePreset(preset: Preset) {
    const range = preset.build();
    setDraft(range);
    applyRange(range);
    setOpen(false);
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            type="button"
            variant="outline"
            aria-label="Date range"
            className={cn('justify-start font-normal', className)}
          >
            <CalendarRange className="mr-2 size-4 opacity-70" aria-hidden="true" />
            <span className="truncate">{triggerLabel}</span>
          </Button>
        }
      />
      <PopoverContent align="start" className="w-auto max-w-[calc(100vw-2rem)] p-0">
        <div className="flex flex-col sm:flex-row">
          <div className="flex flex-row flex-wrap gap-1 border-b p-2 sm:flex-col sm:flex-nowrap sm:border-r sm:border-b-0">
            {DATE_RANGE_PRESETS.map((p) => (
              <Button key={p.label} type="button" variant="ghost" size="sm" className="justify-start" onClick={() => handlePreset(p)}>
                {p.label}
              </Button>
            ))}
          </div>
          <div className="flex flex-col">
            <Calendar
              mode="range"
              numberOfMonths={typeof window !== 'undefined' && window.innerWidth < 640 ? 1 : 2}
              weekStartsOn={1}
              selected={draft}
              onSelect={setDraft}
              defaultMonth={draft?.from ?? new Date()}
            />
            <div className="flex justify-end gap-2 border-t p-2">
              <Button type="button" variant="ghost" size="sm" onClick={() => { setDraft(undefined); applyRange(undefined); setOpen(false); }}>
                Clear
              </Button>
              <Button type="button" size="sm" onClick={() => { applyRange(draft); setOpen(false); }}>
                Apply
              </Button>
            </div>
          </div>
        </div>
      </PopoverContent>
    </Popover>
  );
}
