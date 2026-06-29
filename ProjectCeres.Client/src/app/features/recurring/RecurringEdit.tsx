import { useState, useEffect, useRef } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_ACTIVE_URL, CATEGORIES_ACTIVE_URL, type AccountOptionDto, type CategoryOptionDto } from '../movements/movements-api';
import { RecurringForm, type RecurringFormValues } from './RecurringForm';
import { RECURRING_BY_ID_URL, type RecurringTransactionDetailDto } from './recurring-api';
import type { RecurringPageCtx } from './RecurringCreate';

export function RecurringEdit({ ctx }: { ctx: RecurringPageCtx }) {
  useDocumentTitle('Edit Recurring');
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const headingRef = useRef<HTMLHeadingElement>(null);

  const detail = useApi<RecurringTransactionDetailDto>(RECURRING_BY_ID_URL(id!));
  const accounts = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);
  const categories = useApi<CategoryOptionDto[]>(CATEGORIES_ACTIVE_URL);
  const [values, setValues] = useState<RecurringFormValues | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [notFound, setNotFound] = useState(false);

  useEffect(() => {
    if (detail.data) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- Why: populating edit form when async server data arrives; the form cannot be pre-populated synchronously because the data comes from a network request.
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

  // Focus the heading once when data first arrives. Depending on `values` (an
  // identity that changes on every keystroke) made the effect re-fire on each
  // edit and steal focus from the active input.
  const didFocusHeadingRef = useRef(false);
  useEffect(() => {
    if (values && !didFocusHeadingRef.current) {
      headingRef.current?.focus();
      didFocusHeadingRef.current = true;
    }
  }, [values]);

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
      <div className="space-y-4">
        <p className="text-sm text-muted-foreground">That reminder doesn&apos;t exist.</p>
        <Button variant="outline" onClick={() => navigate('/recurring')}>← Back to Recurring</Button>
      </div>
    );
  }

  if (loading || !values) {
    return (
      <>
        <div className="space-y-1">
          <h1 className="text-3xl font-semibold">Edit reminder</h1>
        </div>
        <div className="space-y-3">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-9 w-full" />)}
        </div>
      </>
    );
  }

  return (
    <>
      <div className="space-y-1">
        <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
          Edit reminder
        </h1>
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
    </>
  );
}
