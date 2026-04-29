import { useState } from 'react';
import { Button } from '@/components/ui/button';

const tokens = [
  { name: '--motion-duration-fast', value: '120ms' },
  { name: '--motion-duration-base', value: '180ms' },
  { name: '--motion-duration-slow', value: '260ms' },
  { name: '--motion-easing-standard', value: 'cubic-bezier(0.2, 0, 0, 1)' },
  { name: '--motion-easing-emphasized', value: 'cubic-bezier(0.3, 0, 0, 1)' },
];

export function Motion() {
  const [on, setOn] = useState(false);
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Motion</h1>
        <p className="mt-2 text-muted-foreground">
          Use the duration and easing tokens for any transition. Never inline a
          numeric duration in a component.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Tokens</h2>
        <table className="w-full max-w-xl text-sm">
          <tbody>
            {tokens.map((t) => (
              <tr key={t.name} className="border-b border-border">
                <td className="py-2 font-mono text-xs">{t.name}</td>
                <td className="py-2 text-muted-foreground">{t.value}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Demo</h2>
        <Button onClick={() => setOn((v) => !v)}>Toggle</Button>
        <div className="mt-4 h-16 w-48 overflow-hidden rounded-md border border-border bg-muted">
          <div
            className="h-full bg-primary"
            style={{
              width: on ? '100%' : '20%',
              transitionProperty: 'width',
              transitionDuration: 'var(--motion-duration-base)',
              transitionTimingFunction: 'var(--motion-easing-standard)',
            }}
          />
        </div>
      </section>
    </div>
  );
}
