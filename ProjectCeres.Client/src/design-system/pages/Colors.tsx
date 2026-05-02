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

const sidebar: Swatch[] = [
  { token: 'sidebar', label: 'Sidebar surface', contrastAgainst: 'sidebar-foreground' },
  { token: 'sidebar-primary', label: 'Active rail', contrastAgainst: 'sidebar-primary-foreground' },
  { token: 'sidebar-accent', label: 'Hover/selected', contrastAgainst: 'sidebar-accent-foreground' },
  { token: 'sidebar-border', label: 'Sidebar border' },
  { token: 'sidebar-ring', label: 'Sidebar focus ring' },
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

      <section>
        <h2 className="mb-4 text-xl font-medium">Sidebar</h2>
        <p className="mb-4 text-sm text-muted-foreground">
          The App Shell sidebar uses a dedicated token group so future re-skins
          can be tuned independently. The layout width{' '}
          <code className="font-mono">--sidebar-w</code> (240px expanded, 56px
          collapsed) is set as a custom property on <code>:root</code> and
          toggled via the <code>sidebar-collapsed</code> class on{' '}
          <code>&lt;html&gt;</code>.
        </p>
        <SwatchGrid swatches={sidebar} />
      </section>
    </div>
  );
}
