import { useRef, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_ACTIVE_URL, CATEGORIES_ACTIVE_URL, type AccountOptionDto, type CategoryOptionDto } from '../movements/movements-api';
import { RecurringForm, type RecurringFormValues } from './RecurringForm';
import { RECURRING_URL } from './recurring-api';

export type RecurringPageCtx = { refetch: () => void; refreshBell: () => void };

function todayIso() {
  return new Date().toISOString().slice(0, 10);
}

const DEFAULTS: RecurringFormValues = {
  name: '', accountId: '', categoryId: '', estimatedAmount: '',
  frequency: 'Monthly', reminderBehaviour: 'SnapToCalendarDay',
  dayOfPeriod: null, nextDueDate: '',
};

export function RecurringCreate({ ctx }: { ctx: RecurringPageCtx }) {
  const navigate = useNavigate();
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  const accounts = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);
  const categories = useApi<CategoryOptionDto[]>(CATEGORIES_ACTIVE_URL);
  const [values, setValues] = useState<RecurringFormValues>(() => ({
    ...DEFAULTS,
    nextDueDate: todayIso(),
  }));
  const [submitting, setSubmitting] = useState(false);

  const loading = accounts.loading || categories.loading;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    try {
      const res = await fetch(RECURRING_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: values.name.trim(),
          estimatedAmount: values.estimatedAmount === '' ? null : Number(values.estimatedAmount),
          accountId: values.accountId,
          categoryId: values.categoryId,
          frequency: values.frequency,
          dayOfPeriod: values.dayOfPeriod,
          nextDueDate: values.nextDueDate,
          reminderBehaviour: values.reminderBehaviour,
        }),
      });
      if (res.ok) {
        toast.success('Created.');
        ctx.refetch();
        ctx.refreshBell();
        navigate('/recurring');
        return;
      }
      toast.error("Couldn't save. Try again.");
    } catch {
      toast.error("Couldn't save. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <div className="mx-auto max-w-3xl space-y-6">
        <div className="space-y-1">
          <h1 className="text-3xl font-semibold">New reminder</h1>
        </div>
        <div className="space-y-3">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-9 w-full" />)}
        </div>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div className="space-y-1">
        <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
          New reminder
        </h1>
        <p className="text-sm text-muted-foreground">
          Set up a template that you can confirm into a real transaction on each due date.
        </p>
      </div>

      <form onSubmit={handleSubmit} className="space-y-5">
        <RecurringForm
          values={values}
          onChange={setValues}
          accounts={accounts.data ?? []}
          categories={categories.data ?? []}
        />
        <div className="flex items-center gap-2 border-t border-border pt-4">
          <Button type="button" variant="outline" onClick={() => navigate('/recurring')}>
            Cancel
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? 'Saving…' : 'Save'}
          </Button>
        </div>
      </form>
    </div>
  );
}
