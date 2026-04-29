const spacingSteps = [0, 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24];
const radii = ['rounded-sm', 'rounded-md', 'rounded-lg', 'rounded-xl', 'rounded-2xl'];
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
              <div className={`bg-primary h-3 w-${n}`} />
              <span className="text-muted-foreground">{n * 0.25}rem</span>
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Radius</h2>
        <div className="flex flex-wrap gap-4">
          {radii.map((r) => (
            <div key={r} className="flex flex-col items-center gap-2">
              <div className={`bg-primary h-16 w-16 ${r}`} />
              <code className="text-xs text-muted-foreground">{r}</code>
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
