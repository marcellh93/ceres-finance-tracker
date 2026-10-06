import { useState } from 'react';
import { Info } from 'lucide-react';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';

/**
 * Small "i" button that reveals `tip`: hover previews it, a click or tap pins it open until an
 * outside click, Esc or a second click. A Popover, not a Tooltip: tooltips never open on touch
 * and close on click. `about` names the button for screen readers.
 */
export function InfoTip({ about, tip }: { about: string; tip: string }) {
  const [open, setOpen] = useState(false);
  const [pinned, setPinned] = useState(false);

  return (
    <Popover
      open={open}
      onOpenChange={(next, details) => {
        if (details.reason === 'trigger-hover') {
          if (!pinned) setOpen(next);
        } else if (details.reason === 'trigger-press') {
          // Base UI reads a press on a hover-opened popover as "close"; here it pins instead.
          setOpen(!pinned);
          setPinned(!pinned);
        } else {
          setOpen(next);
          if (!next) setPinned(false);
        }
      }}
    >
      <PopoverTrigger
        openOnHover
        delay={200}
        aria-label={`About ${about}`}
        className="text-muted-foreground hover:text-foreground transition-colors"
      >
        <Info className="h-3 w-3" />
      </PopoverTrigger>
      <PopoverContent side="top" className="w-auto max-w-xs p-2 text-xs">
        {tip}
      </PopoverContent>
    </Popover>
  );
}
