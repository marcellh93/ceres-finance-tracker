import { useEffect, useRef, useState } from 'react';
import { contrastRatio, formatRatio } from '../lib/contrast';

export type Swatch = {
  /** CSS variable name without leading `--` */
  token: string;
  label: string;
  /** Token whose color this swatch is meant to be readable against */
  contrastAgainst?: string;
};

export function SwatchGrid({ swatches }: { swatches: Swatch[] }) {
  return (
    <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4">
      {swatches.map((s) => (
        <SwatchCard key={s.token} swatch={s} />
      ))}
    </div>
  );
}

function SwatchCard({ swatch }: { swatch: Swatch }) {
  const ref = useRef<HTMLDivElement>(null);
  const [info, setInfo] = useState<{ rgb: string; ratio?: string }>({
    rgb: '—',
  });

  useEffect(() => {
    if (!ref.current) return;
    const bg = getComputedStyle(ref.current).backgroundColor;
    let ratio: string | undefined;
    if (swatch.contrastAgainst) {
      try {
        const probe = document.createElement('div');
        probe.style.color = `var(--${swatch.contrastAgainst})`;
        ref.current.appendChild(probe);
        const fg = getComputedStyle(probe).color;
        ref.current.removeChild(probe);
        ratio = formatRatio(contrastRatio(bg, fg));
      } catch {
        // Browser returned a non-rgb() color (e.g. oklch()) that the
        // contrast helper can't parse yet. Skip the ratio rather than
        // crash the page; track in design-system.md known limitations.
        ratio = undefined;
      }
    }
    setInfo({ rgb: bg, ratio });
  }, [swatch.token, swatch.contrastAgainst]);

  return (
    <div className="overflow-hidden rounded-md border border-border">
      <div
        ref={ref}
        className="h-20"
        style={{ backgroundColor: `var(--${swatch.token})` }}
      />
      <div className="space-y-1 p-3 text-sm">
        <div className="font-medium">{swatch.label}</div>
        <div className="font-mono text-xs text-muted-foreground">
          --{swatch.token}
        </div>
        <div className="font-mono text-xs text-muted-foreground">{info.rgb}</div>
        {info.ratio && (
          <div className="font-mono text-xs text-muted-foreground">
            vs --{swatch.contrastAgainst}: {info.ratio}
          </div>
        )}
      </div>
    </div>
  );
}
