import { SwatchGrid, type Swatch } from '../components/SwatchGrid';

const surface: Swatch[] = [
  { token: 'background', label: 'Background', contrastAgainst: 'foreground' },
  { token: 'card', label: 'Card', contrastAgainst: 'card-foreground' },
  { token: 'popover', label: 'Popover', contrastAgainst: 'popover-foreground' },
  { token: 'muted', label: 'Muted', contrastAgainst: 'muted-foreground' },
  { token: 'accent', label: 'Accent', contrastAgainst: 'accent-foreground' },
  { token: 'border', label: 'Border' },
];

const brand: Swatch[] = [
  { token: 'primary', label: 'Primary', contrastAgainst: 'primary-foreground' },
  { token: 'secondary', label: 'Secondary', contrastAgainst: 'secondary-foreground' },
];

const semantic: Swatch[] = [
  { token: 'success', label: 'Success (income)', contrastAgainst: 'background' },
  { token: 'destructive', label: 'Destructive (expense)', contrastAgainst: 'background' },
  { token: 'warning', label: 'Warning', contrastAgainst: 'background' },
  { token: 'info', label: 'Info', contrastAgainst: 'background' },
];

export function Colors() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Colors</h1>
        <p className="mt-2 text-muted-foreground">
          All colors are CSS custom properties on <code>:root</code> and{' '}
          <code>.dark</code>. Components read them via Tailwind utilities like{' '}
          <code>bg-primary</code> or <code>text-muted-foreground</code>. Never
          hard-code hex values in components.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Surface</h2>
        <SwatchGrid swatches={surface} />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Brand</h2>
        <SwatchGrid swatches={brand} />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Semantic</h2>
        <SwatchGrid swatches={semantic} />
      </section>
    </div>
  );
}
