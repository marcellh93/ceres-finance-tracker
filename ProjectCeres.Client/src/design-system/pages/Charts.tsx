import { Bar, BarChart, CartesianGrid, ResponsiveContainer, XAxis, YAxis } from 'recharts';
import { SwatchGrid, type Swatch } from '../components/SwatchGrid';

const chartSwatches: Swatch[] = Array.from({ length: 8 }, (_, i) => ({
  token: `chart-${i + 1}`,
  label: `Series ${i + 1}`,
  contrastAgainst: 'background',
}));

const sample = [
  { month: 'Jan', a: 400, b: 240, c: 180 },
  { month: 'Feb', a: 320, b: 300, c: 220 },
  { month: 'Mar', a: 500, b: 280, c: 260 },
  { month: 'Apr', a: 470, b: 350, c: 300 },
];

export function Charts() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Chart Palette</h1>
        <p className="mt-2 text-muted-foreground">
          Eight qualitative colors derived from the brand palette, all WCAG AA
          against the background in both light and dark modes.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Swatches</h2>
        <SwatchGrid swatches={chartSwatches} />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Sample bar chart</h2>
        <div className="h-72 w-full max-w-2xl">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={sample}>
              <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
              <XAxis dataKey="month" stroke="var(--muted-foreground)" />
              <YAxis stroke="var(--muted-foreground)" />
              <Bar dataKey="a" fill="var(--chart-1)" />
              <Bar dataKey="b" fill="var(--chart-2)" />
              <Bar dataKey="c" fill="var(--chart-3)" />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </section>
    </div>
  );
}
