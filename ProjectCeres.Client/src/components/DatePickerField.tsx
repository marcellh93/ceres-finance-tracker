import { useState } from 'react';
import { CalendarIcon } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Calendar } from '@/components/ui/calendar';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { formatDate } from '@/app/lib/date-format';
import { useSettings } from '@/app/lib/use-settings';

type Props = {
  /** ISO yyyy-MM-dd or null for empty. */
  value: string | null;
  onChange: (next: string | null) => void;
  placeholder?: string;
  required?: boolean;
  id?: string;
  /** When true, keeps "Clear" button hidden (e.g., for required fields). */
  hideClear?: boolean;
};

export function DatePickerField({
  value,
  onChange,
  placeholder = 'Pick a date',
  id,
  hideClear,
}: Props) {
  const [open, setOpen] = useState(false);
  const settings = useSettings();
  const dateFormat = settings.data?.dateFormat;

  const selectedDate = value ? parseIsoDate(value) : undefined;

  function handleSelect(date: Date | undefined) {
    if (!date) {
      onChange(null);
    } else {
      onChange(toIsoDate(date));
    }
    setOpen(false);
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id}
            type="button"
            variant="outline"
            className={cn('w-full justify-start font-normal', !value && 'text-muted-foreground')}
          >
            <CalendarIcon className="mr-2 h-4 w-4" />
            {value ? formatDate(value, dateFormat) : placeholder}
          </Button>
        }
      />
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar mode="single" selected={selectedDate} onSelect={handleSelect} />
        {!hideClear && value && (
          <div className="border-t p-2">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="w-full"
              onClick={() => {
                onChange(null);
                setOpen(false);
              }}
            >
              Clear
            </Button>
          </div>
        )}
      </PopoverContent>
    </Popover>
  );
}

function parseIsoDate(iso: string): Date | undefined {
  const [y, m, d] = iso.split('-').map(Number);
  if (!y || !m || !d) return undefined;
  return new Date(y, m - 1, d);
}

function toIsoDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}
