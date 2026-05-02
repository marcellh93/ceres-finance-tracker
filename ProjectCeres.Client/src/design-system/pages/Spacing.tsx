const spacingSteps = [0, 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24];

type Radius = { class: string; multiplier: string; size: string };
const radii: Radius[] = [
  { class: 'rounded-sm', multiplier: '0.6×', size: '0.375rem' },
  { class: 'rounded-md', multiplier: '0.8×', size: '0.5rem' },
  { class: 'rounded-lg', multiplier: '1.0×', size: '0.625rem' },
  { class: 'rounded-xl', multiplier: '1.4×', size: '0.875rem' },
  { class: 'rounded-2xl', multiplier: '1.8×', size: '1.125rem' },
  { class: 'rounded-3xl', multiplier: '2.2×', size: '1.375rem' },
  { class: 'rounded-4xl', multiplier: '2.6×', size: '1.625rem' },
];

const shadows = ['shadow-sm', 'shadow', 'shadow-md', 'shadow-lg'];

export function Spacing() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Spacing, Radius & Shadow</h1>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Spacing scale (Tailwind defaults)</h2>
        <div className="space-y-2">
          {spacingSteps.map((n) => (
            <div key={n} className="flex items-center gap-3 text-sm">
              <code className="w-12 text-muted-foreground">p-{n}</code>
              <div
                className="bg-primary h-3"
                style={{ width: `${n * 0.25}rem` }}
              />
              <span className="text-muted-foreground">{n * 0.25}rem</span>
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Radius</h2>
        <p className="mb-4 text-sm text-muted-foreground">
          Base is <code className="font-mono">--radius: 0.625rem</code> (10px).
          Every step is computed from that — retune the base in{' '}
          <code className="font-mono">index.css</code> to scale every rounded
          utility at once. <code className="font-mono">rounded-4xl</code> is
          what <code className="font-mono">&lt;Badge&gt;</code> uses for its
          pill shape.
        </p>
        <div className="flex flex-wrap gap-6">
          {radii.map((r) => (
            <div key={r.class} className="flex flex-col items-center gap-2">
              <div className={`bg-primary h-16 w-16 ${r.class}`} />
              <code className="text-xs text-muted-foreground">{r.class}</code>
              <span className="text-xs text-muted-foreground">
                {r.multiplier} · {r.size}
              </span>
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Shadow</h2>
        <div className="flex flex-wrap gap-6">
          {shadows.map((s) => (
            <div key={s} className="flex flex-col items-center gap-2">
              <div className={`bg-card h-20 w-32 rounded-md ${s}`} />
              <code className="text-xs text-muted-foreground">{s}</code>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
