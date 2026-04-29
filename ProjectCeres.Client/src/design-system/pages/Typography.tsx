import { Numeric } from '@/components/Numeric';

const typeScale = [
  { className: 'text-xs',  label: 'xs  · 12px / Caption' },
  { className: 'text-sm',  label: 'sm  · 14px / Body small' },
  { className: 'text-base',label: 'base · 16px / Body' },
  { className: 'text-lg',  label: 'lg  · 18px / Lead' },
  { className: 'text-xl',  label: 'xl  · 20px / Subhead' },
  { className: 'text-2xl', label: '2xl · 24px / H3' },
  { className: 'text-3xl', label: '3xl · 30px / H2' },
  { className: 'text-4xl', label: '4xl · 36px / H1' },
];

export function Typography() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Typography</h1>
        <p className="mt-2 text-muted-foreground">
          <strong>Inter</strong> for all UI text and prose.{' '}
          <strong>IBM Plex Mono</strong> via the <code>&lt;Numeric&gt;</code>{' '}
          component for currency, percentages, and dates in tabular contexts.
          Percentages embedded in sentences stay in Inter.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Sans (Inter) — type scale</h2>
        <div className="space-y-3">
          {typeScale.map((t) => (
            <div key={t.className} className={t.className}>
              {t.label} — The quick brown fox jumps over the lazy dog.
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Mono (IBM Plex Mono) — Numeric</h2>
        <table className="w-full max-w-md border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left">
              <th className="py-2">Account</th>
              <th className="py-2 text-right">Balance</th>
              <th className="py-2 text-right">% of total</th>
            </tr>
          </thead>
          <tbody>
            {[
              ['Checking',   '€1.234,56',  '42%'],
              ['Savings',    '€8.500,00',  '38%'],
              ['Credit card','-€512,30',   '20%'],
            ].map(([account, balance, pct]) => (
              <tr key={account} className="border-b border-border">
                <td className="py-2">{account}</td>
                <td className="py-2 text-right"><Numeric>{balance}</Numeric></td>
                <td className="py-2 text-right"><Numeric>{pct}</Numeric></td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Mixed in prose (stays Inter)</h2>
        <p className="max-w-prose text-base">
          You've spent 73% of your Groceries budget so far this month —
          on track to finish around 95% by the period end. The percentages
          here are part of the sentence and stay in Inter for readability.
        </p>
      </section>
    </div>
  );
}
