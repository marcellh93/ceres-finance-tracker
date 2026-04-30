import { toast, Toaster } from 'sonner';
import { Button } from '@/components/ui/button';

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
        <div className="flex flex-wrap gap-3">
          <Button onClick={() => toast.success('Saved.')}>
            Success
          </Button>
          <Button onClick={() => toast.error("Couldn't save. Try again.")}>
            Error
          </Button>
          <Button onClick={() => toast.info('Heads up.')}>
            Info
          </Button>
          <Button onClick={() => toast.warning('Watch out.')}>
            Warning
          </Button>
          <Button onClick={() => toast('Plain notification.')}>
            Plain
          </Button>
        </div>
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

      <Toaster />
    </div>
  );
}
