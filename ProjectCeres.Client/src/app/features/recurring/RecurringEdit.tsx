import { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_URL, type AccountListItemDto } from '../accounts/accounts-api';
import { RecurringForm, type RecurringFormValues } from './RecurringForm';
import { RECURRING_BY_ID_URL, type RecurringTransactionDetailDto } from './recurring-api';
import type { RecurringPageCtx } from './RecurringCreate';

export function RecurringEdit({ ctx }: { ctx: RecurringPageCtx }) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const detail = useApi<RecurringTransactionDetailDto>(RECURRING_BY_ID_URL(id!));
  const accounts = useApi<AccountListItemDto[]>(ACCOUNTS_URL);
  const categories = useApi<Array<{ id: string; name: string }>>('/api/categories');
  const [values, setValues] = useState<RecurringFormValues | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [notFound, setNotFound] = useState(false);

  useEffect(() => {
    if (detail.data) {
      setValues({
        name: detail.data.name,
        accountId: detail.data.accountId,
        categoryId: detail.data.categoryId,
        estimatedAmount: detail.data.estimatedAmount?.toString() ?? '',
        frequency: detail.data.frequency,
        reminderBehaviour: detail.data.reminderBehaviour,
        dayOfPeriod: detail.data.dayOfPeriod,
        nextDueDate: detail.data.nextDueDate,
      });
      setNotFound(false);
    }
    if (detail.error) setNotFound(true);
  }, [detail.data, detail.error]);

  const loading = detail.loading || accounts.loading || categories.loading;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!values) return;
    setSubmitting(true);
    try {
      const res = await fetch(RECURRING_BY_ID_URL(id!), {
        method: 'PATCH',
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
        toast.success('Saved.');
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

  if (notFound) {
    return (
      <Card>
        <CardContent className="pt-6 space-y-3">
          <p>That reminder doesn&apos;t exist.</p>
          <Button variant="outline" onClick={() => navigate('/recurring')}>← Back to Recurring</Button>
        </CardContent>
      </Card>
    );
  }

  if (loading || !values) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit reminder</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-9 w-full" />)}
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader><CardTitle>Edit reminder</CardTitle></CardHeader>
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
