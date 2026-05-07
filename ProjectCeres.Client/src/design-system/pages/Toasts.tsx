import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Toaster } from '@/components/ui/sonner';

export function Toasts() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Toasts</h1>
        <p className="mt-2 text-muted-foreground">
          Sonner is the SPA-wide toast system. A single <code>&lt;Toaster /&gt;</code> is
          mounted in <code>AppLayout.tsx</code>; this page mounts a local one for demos.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Variants</h2>
        <VariantsSection />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Stacking</h2>
        <p className="mb-3 text-sm text-muted-foreground">
          Sonner queues multiple toasts. Click to fire 5 in quick succession.
        </p>
        <Button
          onClick={() => {
            for (let i = 1; i <= 5; i++) {
              setTimeout(() => toast.success(`Toast ${i} of 5`), i * 100);
            }
          }}
        >
          Fire 5 stacked toasts
        </Button>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">
          The 422 carve-out — never toast a validation failure
        </h2>
        <p className="mb-4 text-sm text-muted-foreground">
          Validation errors render inline next to the field — never as a toast.
          A toast that says "Validation failed" tells the user nothing they can
          act on. The correction is already keyed off the field they were
          editing.
        </p>
        <div className="grid gap-4 md:grid-cols-2">
          <div className="rounded-md border border-success/30 bg-success/5 p-4">
            <div className="mb-3 text-xs font-medium uppercase tracking-wide text-success">
              Correct
            </div>
            <ValidationDemoCorrect />
          </div>
          <div className="rounded-md border border-destructive/30 bg-destructive/5 p-4">
            <div className="mb-3 text-xs font-medium uppercase tracking-wide text-destructive">
              Don't do this
            </div>
            <ValidationDemoWrong />
          </div>
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">
          Partial-success batches — summarise, don't spam
        </h2>
        <p className="mb-3 text-sm text-muted-foreground">
          When some items succeed and some fail, fire one{' '}
          <code className="font-mono">toast.warning(...)</code> summarising —
          not one toast per failure. The user reads a stack of three toasts as
          three problems, not as one batch with one issue.
        </p>
        <div className="flex flex-wrap gap-3">
          <Button
            onClick={() =>
              toast.warning('2 of 3 attachments uploaded. 1 failed.')
            }
          >
            Summary toast (correct)
          </Button>
          <Button
            variant="outline"
            onClick={() => {
              toast.success('file-a.pdf uploaded.');
              setTimeout(() => toast.success('file-b.pdf uploaded.'), 80);
              setTimeout(() => toast.error('file-c.pdf failed.'), 160);
            }}
          >
            One toast per file (anti-pattern)
          </Button>
        </div>
      </section>

      <Toaster />
    </div>
  );
}

function VariantsSection() {
  const [loadingId, setLoadingId] = useState<string | number | null>(null);

  function fireLoading() {
    if (loadingId !== null) toast.dismiss(loadingId);
    const id = toast.loading('Working on it… (60s)', { duration: 60_000 });
    setLoadingId(id);
  }

  function dismissLoading() {
    if (loadingId !== null) {
      toast.dismiss(loadingId);
      setLoadingId(null);
    }
  }

  return (
    <div className="flex flex-wrap gap-3">
      <Button onClick={() => toast.success('Saved.')}>Success</Button>
      <Button onClick={() => toast.error("Couldn't save. Try again.")}>
        Error
      </Button>
      <Button onClick={() => toast.info('Heads up.')}>Info</Button>
      <Button onClick={() => toast.warning('Watch out.')}>Warning</Button>
      <Button onClick={() => toast('Plain notification.')}>Plain</Button>
      <Button variant="outline" onClick={fireLoading}>
        Loading (60s)
      </Button>
      <Button
        variant="ghost"
        onClick={dismissLoading}
        disabled={loadingId === null}
      >
        Dismiss loading
      </Button>
    </div>
  );
}

function ValidationDemoCorrect() {
  const [amount, setAmount] = useState('');
  const [error, setError] = useState<string | undefined>(undefined);

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const n = Number(amount);
    if (!amount || Number.isNaN(n) || n <= 0) {
      // Inline-only — no toast.
      setError('Must be greater than 0.');
      return;
    }
    setError(undefined);
    toast.success('Saved.');
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <div className="space-y-1.5">
        <Label htmlFor="amount-correct">Amount</Label>
        <Input
          id="amount-correct"
          inputMode="decimal"
          placeholder="0.00"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
        />
        {error && <p className="text-xs text-destructive">{error}</p>}
      </div>
      <Button type="submit" size="sm">Save</Button>
    </form>
  );
}

function ValidationDemoWrong() {
  const [amount, setAmount] = useState('');

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const n = Number(amount);
    if (!amount || Number.isNaN(n) || n <= 0) {
      // Anti-pattern: vague toast with no field context.
      toast.error('Validation failed.');
      return;
    }
    toast.success('Saved.');
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <div className="space-y-1.5">
        <Label htmlFor="amount-wrong">Amount</Label>
        <Input
          id="amount-wrong"
          inputMode="decimal"
          placeholder="0.00"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
        />
      </div>
      <Button type="submit" size="sm">Save</Button>
    </form>
  );
}
