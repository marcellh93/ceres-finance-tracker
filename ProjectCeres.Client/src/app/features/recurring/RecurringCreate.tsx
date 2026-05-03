import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_URL, type AccountListItemDto } from '../accounts/accounts-api';
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
  const accounts = useApi<AccountListItemDto[]>(ACCOUNTS_URL);
  const categories = useApi<Array<{ id: string; name: string }>>('/api/categories');
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
      <Card>
        <CardHeader><CardTitle>New reminder</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-9 w-full" />)}
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader><CardTitle>New reminder</CardTitle></CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit} className="space-y-4">
          <RecurringForm
            values={values}
            onChange={setValues}
            accounts={accounts.data ?? []}
            categories={categories.data ?? []}
          />
          <div className="flex gap-2 pt-2">
            <Button type="submit" disabled={submitting}>
              {submitting ? 'Saving…' : 'Save'}
            </Button>
            <Button type="button" variant="outline" onClick={() => navigate('/recurring')}>
              Cancel
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
