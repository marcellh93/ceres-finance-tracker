import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Numeric } from '@/components/Numeric';
import { StatTile } from '@/components/StatTile';
import { StatRow } from '@/components/StatRow';
import { EquationRow } from '@/components/EquationRow';
import { Tile } from '@/components/Tile';
import { CardError } from '@/app/components/CardError';

export function Patterns() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Patterns</h1>
        <p className="mt-2 text-muted-foreground">
          SPA-specific primitives for layout, stats, and error states.
          Use these instead of inventing one-off components.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Layout primitives</h2>
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <Card>
            <CardHeader><CardTitle>StatTile (vertical)</CardTitle></CardHeader>
            <CardContent>
              <StatTile
                label="Net Worth"
                value={<Numeric>€ 1,234.56</Numeric>}
              />
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>StatRow (inline)</CardTitle></CardHeader>
            <CardContent>
              <dl className="space-y-2">
                <StatRow label="Income" value={<Numeric className="text-success">€ 3,200.00</Numeric>} />
                <StatRow label="Expenses" value={<Numeric className="text-destructive">€ 1,850.45</Numeric>} />
                <StatRow label="Savings Rate" value={<Numeric>42.2%</Numeric>} />
              </dl>
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>EquationRow (compact)</CardTitle></CardHeader>
            <CardContent>
              <div className="space-y-1">
                <EquationRow label="Liquid" value={<Numeric>€ 1,200.20</Numeric>} />
                <EquationRow label="Bills due (7 days)" value={<Numeric>−€ 770.00</Numeric>} />
                <EquationRow
                  label="Available today"
                  value={<Numeric className="text-base font-bold text-success">€ 430.20</Numeric>}
                />
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>Tile (KPI surface)</CardTitle></CardHeader>
            <CardContent>
              <div className="grid grid-cols-3 gap-3">
                <Tile>
                  <StatTile label="Income" value={<Numeric className="text-2xl text-success">€ 3,200</Numeric>} />
                </Tile>
                <Tile>
                  <StatTile label="Expenses" value={<Numeric className="text-2xl text-destructive">€ 1,850</Numeric>} />
                </Tile>
                <Tile>
                  <StatTile label="Savings" value={<Numeric className="text-2xl">42.2%</Numeric>} />
                </Tile>
              </div>
            </CardContent>
          </Card>
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">CardError</h2>
        <p className="mb-3 text-sm text-muted-foreground">
          Standard error+retry UI for any card whose data fetch fails.
          Pass <code>section</code> (the noun used in the error message) and
          an <code>onRetry</code> handler.
        </p>
        <Card className="max-w-md">
          <CardHeader><CardTitle>Net Worth Over Time</CardTitle></CardHeader>
          <CardContent>
            <CardError
              section="Net Worth Over Time"
              onRetry={() => alert('Retry clicked')}
            />
          </CardContent>
        </Card>
      </section>
    </div>
  );
}
