import { useState } from 'react';
import { CheckCircle2, Clock, Info, Wallet } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { Numeric } from '@/components/Numeric';
import { StatTile } from '@/components/StatTile';
import { StatRow } from '@/components/StatRow';
import { EquationRow } from '@/components/EquationRow';
import { Tile } from '@/components/Tile';
import { CardError } from '@/app/components/CardError';
import { AttachmentDropzone } from '@/app/features/movements/AttachmentDropzone';

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
        <h2 className="mb-4 text-xl font-medium">App Shell anatomy</h2>
        <p className="mb-4 text-sm text-muted-foreground">
          Every authenticated page renders inside this grammar: 3.5rem TopBar,{' '}
          <code className="font-mono">--sidebar-w</code>-wide Sidebar (240px /
          56px collapsed), and a scrollable main area. Active sidebar items use
          an inset shadow rail, not a left border, so the row doesn't shift.
        </p>
        <AppShellDiagram />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Forms — Field + inline error + form-error banner</h2>
        <p className="mb-4 text-sm text-muted-foreground">
          Per-field 422 errors render inline beneath the control. Form-level
          errors (submit failures, cross-field issues) render in a banner above
          the actions. Never route a 422 with a field key to the banner.
        </p>
        <FormDemo />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Status block — success / idle side panel</h2>
        <p className="mb-4 text-sm text-muted-foreground">
          A binary-state recipe: icon + heading + caption swap colours when the
          Switch flips. Today's "Cleared" toggle; tomorrow's "Reconciled?",
          "Confirmed?" surfaces.
        </p>
        <StatusBlockDemo />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Tooltipped panel labels</h2>
        <p className="mb-4 text-sm text-muted-foreground">
          Uppercase tracked labels with an optional <code>?</code> Info-tooltip.
          Hover the icon to read the explainer. Class shape{' '}
          <code className="font-mono">text-xs uppercase tracking-wider text-muted-foreground font-medium</code>{' '}
          is fixed — every panel label uses it.
        </p>
        <TooltippedLabelsDemo />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Panel empty states</h2>
        <p className="mb-4 text-sm text-muted-foreground">
          Two shapes — pick by information density. Visual block (icon + caption,
          centred) when the panel would otherwise be a large blank. Compact note
          (italic muted text) when the empty state is a side note next to other
          content.
        </p>
        <PanelEmptyDemo />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">AttachmentDropzone</h2>
        <p className="mb-4 text-sm text-muted-foreground">
          Reusable file-upload surface: dashed-border drop area, hidden native
          input + visible button, drag-state tint of{' '}
          <code className="font-mono">border-primary bg-primary/5</code>. Demo
          mounts in <code className="font-mono">create</code> mode so files
          buffer locally without hitting the API.
        </p>
        <Card className="max-w-2xl">
          <CardHeader>
            <CardTitle>Attachments</CardTitle>
          </CardHeader>
          <CardContent>
            <AttachmentDropzone
              mode="create"
              parentType="Transaction"
              embedded
            />
          </CardContent>
        </Card>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">View transition naming</h2>
        <p className="mb-4 text-sm text-muted-foreground">
          Coherent slot names let row-to-form navigation cross-fade their
          bounding boxes. Per-row slot:{' '}
          <code className="font-mono">{'<feature>-row-<id>'}</code>. Per-form
          slot: <code className="font-mono">{'<feature>-form'}</code>. Pick names
          that follow this shape so future shared-element transitions group
          correctly.
        </p>
        <pre className="overflow-x-auto rounded-md border border-border bg-muted/40 p-4 text-xs leading-relaxed">
{`// On each <tr> in the Movements table:
<tr style={{ viewTransitionName: \`movement-row-\${id}\` }}>…</tr>

// On the Movements edit-form root:
<form style={{ viewTransitionName: 'movement-form' }}>…</form>`}
        </pre>
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

// ---------------------------------------------------------------------------
// App Shell anatomy diagram (static, labeled)
// ---------------------------------------------------------------------------

function AppShellDiagram() {
  return (
    <div className="rounded-md border border-border bg-card overflow-hidden max-w-3xl">
      {/* TopBar */}
      <div className="flex h-14 items-center justify-between border-b border-border bg-muted/30 px-4">
        <div className="text-sm font-semibold">BrandMark</div>
        <div className="flex items-center gap-3">
          <div className="rounded-md border border-input px-2 py-1 text-xs text-muted-foreground">
            Search… <span className="ml-1 rounded bg-muted px-1 font-mono">⌘K</span>
          </div>
          <div className="h-7 w-7 rounded-full bg-muted" />
        </div>
      </div>
      {/* Body */}
      <div className="grid grid-cols-[240px_1fr]">
        {/* Sidebar */}
        <aside className="border-r border-border bg-muted/10 p-3">
          <div className="space-y-1">
            <div className="rounded-md bg-accent text-accent-foreground shadow-[inset_2px_0_0_var(--primary)] px-3 py-2 text-sm">
              Dashboard
            </div>
            <div className="rounded-md px-3 py-2 text-sm text-muted-foreground">Movements</div>
            <div className="rounded-md px-3 py-2 text-sm text-muted-foreground">Budgets</div>
            <div className="rounded-md px-3 py-2 text-sm text-muted-foreground">Reports</div>
          </div>
        </aside>
        {/* Main */}
        <div className="min-h-[200px] p-6">
          <div className="text-xs uppercase tracking-wider text-muted-foreground font-medium">
            Main content area
          </div>
          <div className="mt-2 text-sm text-muted-foreground">
            Routed page mounts here. Scrolls independently of the shell.
          </div>
        </div>
      </div>
      {/* Annotations */}
      <div className="border-t border-border bg-muted/30 px-4 py-2 font-mono text-[11px] text-muted-foreground">
        grid-rows-[3.5rem_1fr] · grid-cols-[var(--sidebar-w,240px)_1fr] · skip-link first child of root · &lt;Toaster /&gt; mounted once
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Form demo — Field + inline error + form-error banner
// ---------------------------------------------------------------------------

function FormDemo() {
  const [amount, setAmount] = useState('');
  const [errors, setErrors] = useState<{ amount?: string }>({});
  const [formError, setFormError] = useState<string | undefined>(undefined);

  function reset() {
    setAmount('');
    setErrors({});
    setFormError(undefined);
  }

  function trigger422() {
    setFormError(undefined);
    setErrors({ amount: 'Must be greater than 0.' });
  }

  function triggerFormError() {
    setErrors({});
    setFormError('Could not save — the server is unreachable. Try again.');
  }

  return (
    <Card className="max-w-md">
      <CardHeader>
        <CardTitle>Edit transaction</CardTitle>
      </CardHeader>
      <CardContent>
        <form
          onSubmit={(e) => e.preventDefault()}
          className="space-y-4"
        >
          {formError && (
            <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
              {formError}
            </div>
          )}

          <Field label="Amount" htmlFor="patterns-amount" error={errors.amount}>
            <Input
              id="patterns-amount"
              inputMode="decimal"
              placeholder="0.00"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
          </Field>

          <Field label="Description" htmlFor="patterns-desc">
            <Input id="patterns-desc" placeholder="(optional)" />
          </Field>

          <div className="flex flex-wrap gap-2 pt-2">
            <Button type="button" size="sm" onClick={trigger422}>
              Trigger 422 (inline)
            </Button>
            <Button type="button" size="sm" variant="outline" onClick={triggerFormError}>
              Trigger form error (banner)
            </Button>
            <Button type="button" size="sm" variant="ghost" onClick={reset}>
              Reset
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function Field({
  label,
  htmlFor,
  error,
  children,
}: {
  label: string;
  htmlFor?: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      {htmlFor ? (
        <Label htmlFor={htmlFor}>{label}</Label>
      ) : (
        <div className="flex items-center gap-2 text-sm leading-none font-medium select-none">
          {label}
        </div>
      )}
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Status block demo
// ---------------------------------------------------------------------------

function StatusBlockDemo() {
  const [isCleared, setIsCleared] = useState(false);

  return (
    <div className="max-w-md">
      <div
        className={
          'flex items-start justify-between gap-4 rounded-md border p-4 transition-colors duration-200 ' +
          (isCleared
            ? 'border-success/30 bg-success/10'
            : 'border-border bg-muted/30')
        }
      >
        <div className="flex items-start gap-3">
          <div
            className={
              'mt-0.5 transition-colors duration-200 ' +
              (isCleared ? 'text-success' : 'text-muted-foreground')
            }
            aria-hidden="true"
          >
            {isCleared ? <CheckCircle2 className="h-5 w-5" /> : <Clock className="h-5 w-5" />}
          </div>
          <div className="space-y-0.5">
            <div
              className={
                'text-sm font-medium tracking-wide transition-colors duration-200 ' +
                (isCleared ? 'text-success' : 'text-foreground/80')
              }
            >
              Status
            </div>
            <p
              className={
                'text-xs transition-colors duration-200 ' +
                (isCleared ? 'text-success/80' : 'text-muted-foreground')
              }
            >
              {isCleared ? 'Cleared the bank.' : "Hasn't cleared the bank yet."}
            </p>
          </div>
        </div>
        <Switch
          checked={isCleared}
          onCheckedChange={setIsCleared}
          aria-label="Cleared"
        />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Tooltipped panel labels demo
// ---------------------------------------------------------------------------

function TooltippedLabelsDemo() {
  return (
    <Card className="max-w-2xl">
      <CardContent className="space-y-6 pt-6">
        <div>
          <PanelLabel>Spendable Balance</PanelLabel>
          <div className="text-2xl font-semibold">$1,200.20</div>
        </div>
        <div>
          <PanelLabel tooltip="What's free to spend right now after upcoming bills and your category budgets.">
            Spendable Balance
          </PanelLabel>
          <div className="text-2xl font-semibold">$1,200.20</div>
        </div>
        <div>
          <PanelLabel tooltip="Months of expenses your liquid savings could cover at current burn.">
            Runway
          </PanelLabel>
          <div className="text-2xl font-semibold">4.3 months</div>
        </div>
      </CardContent>
    </Card>
  );
}

function PanelLabel({
  children,
  tooltip,
}: {
  children: string;
  tooltip?: string;
}) {
  return (
    <div className="text-xs uppercase tracking-wider text-muted-foreground font-medium mb-2 flex items-center gap-1.5">
      <span>{children}</span>
      {tooltip && (
        <TooltipProvider delay={200}>
          <Tooltip>
            <TooltipTrigger
              render={
                <button
                  type="button"
                  aria-label={`About ${children}`}
                  className="text-muted-foreground hover:text-foreground transition-colors"
                >
                  <Info className="h-3 w-3" />
                </button>
              }
            />
            <TooltipContent className="max-w-xs">{tooltip}</TooltipContent>
          </Tooltip>
        </TooltipProvider>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Panel empty states demo
// ---------------------------------------------------------------------------

function PanelEmptyDemo() {
  return (
    <div className="grid max-w-3xl gap-4 sm:grid-cols-2">
      <Card>
        <CardContent className="pt-6">
          <PanelLabel>Spendable Balance</PanelLabel>
          <PanelEmpty icon={<Wallet className="h-6 w-6" />}>
            No asset accounts found
          </PanelEmpty>
        </CardContent>
      </Card>
      <Card>
        <CardContent className="pt-6">
          <PanelLabel>Income Δ</PanelLabel>
          <div className="text-2xl font-semibold">—</div>
          <PanelEmpty>Not enough history yet</PanelEmpty>
        </CardContent>
      </Card>
    </div>
  );
}

function PanelEmpty({
  icon,
  children,
}: {
  icon?: React.ReactNode;
  children: string;
}) {
  if (icon) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 min-h-[100px] py-2 text-center">
        <div className="text-muted-foreground" aria-hidden="true">
          {icon}
        </div>
        <p className="text-xs text-muted-foreground max-w-[14rem]">{children}</p>
      </div>
    );
  }
  return <div className="text-xs italic text-muted-foreground">{children}</div>;
}
