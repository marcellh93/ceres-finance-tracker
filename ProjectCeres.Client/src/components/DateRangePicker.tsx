import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { DateRange } from 'react-day-picker';
import { CalendarRange } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Calendar } from '@/components/ui/calendar';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { useSettings } from '../app/lib/use-settings';
import {
  toIsoDate,
  readDraftFromParams,
  formatRangeLabel,
  DATE_RANGE_PRESETS,
} from './date-range-picker-utils';

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
    // eslint-disable-next-line react-hooks/set-state-in-effect -- Why: syncing draft picker state from URL params when fromKey/toKey change externally (e.g. navigation); this is a "sync with external URL state" pattern.
    setDraft(readDraftFromParams(params, fromKey, toKey));
    // eslint-disable-next-line react-hooks/exhaustive-deps -- Why: only the URL param values (fromParam, toParam) drive the draft sync; including params object or fromKey/toKey would cause stale-closure issues.
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

  function handlePreset(preset: (typeof DATE_RANGE_PRESETS)[number]) {
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
