import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useOutletContext, useSearchParams } from 'react-router-dom';
import { toast } from 'sonner';
import { MovementForm, type MovementFormValues } from './MovementForm';
import { AttachmentDropzone } from './AttachmentDropzone';
import {
  ACCOUNTS_ACTIVE_URL,
  CATEGORIES_ACTIVE_URL,
  LIABILITY_PAYMENTS_CREATE_URL,
  TRANSACTIONS_CREATE_URL,
  TRANSFERS_CREATE_URL,
  type AccountOptionDto,
  type CategoryOptionDto,
  type MovementType,
} from './movements-api';
import { parseValidationErrors } from './movement-validation';
import { useApi } from '../../lib/use-api';

function urlToMovementType(t: string | null): MovementType | null {
  if (t === 'transaction') return 'Transaction';
  if (t === 'transfer') return 'Transfer';
  if (t === 'liabilitypayment') return 'LiabilityPayment';
  return null;
}

export function MovementCreate() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { refetch } = useOutletContext<{ refetch: () => void }>();

  const typeParam = searchParams.get('type');
  const movementType = urlToMovementType(typeParam);

  const [pendingAttachments, setPendingAttachments] = useState<File[]>([]);

  // No type in URL → bounce back to the list. The `+ New` dropdown is the only
  // entry point and it always sets ?type=…; a bare /movements/new visit is invalid.
  useEffect(() => {
    if (!movementType) navigate('/movements', { replace: true });
  }, [movementType, navigate]);

  const { data: accounts } = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);
  const { data: categories } = useApi<CategoryOptionDto[]>(CATEGORIES_ACTIVE_URL);

  // Narrow account options to the active currency (the tab the user came
  // from). Mixing currencies inside a single movement is rejected by the
  // server (transfers must share a currency, liability payments too) and
  // a transaction can only sit in one account, so showing other-currency
  // accounts in the picker is just noise.
  const activeCurrency = searchParams.get('currency');
  const narrowedAccounts = (accounts ?? []).filter(
    (a) => !activeCurrency || a.currencyCode === activeCurrency,
  );

  const initialValues = useMemo<MovementFormValues>(
    () => ({
      date: new Date().toISOString().slice(0, 10),
      amount: '',
      description: '',
      accountId: null,
      categoryId: null,
      sourceAccountId: null,
      destAccountId: null,
      assetAccountId: null,
      liabilityAccountId: null,
      isCleared: false,
      budgetId: null,
      needsReview: false,
    }),
    [],
  );

  async function onSubmit(
    values: MovementFormValues,
  ): Promise<{ ok: true } | { ok: false; errors: Record<string, string> }> {
    if (!movementType) {
      return { ok: false, errors: { _form: 'No movement type selected.' } };
    }

    const url =
      movementType === 'Transaction'
        ? TRANSACTIONS_CREATE_URL
        : movementType === 'Transfer'
          ? TRANSFERS_CREATE_URL
          : LIABILITY_PAYMENTS_CREATE_URL;

    const body =
      movementType === 'Transaction'
        ? {
            date: values.date,
            amount: Number(values.amount),
            accountId: values.accountId,
            categoryId: values.categoryId,
            description: values.description || null,
          }
        : movementType === 'Transfer'
          ? {
              date: values.date,
              amount: Number(values.amount),
              sourceAccountId: values.sourceAccountId,
              destAccountId: values.destAccountId,
              description: values.description || null,
            }
          : {
              date: values.date,
              amount: Number(values.amount),
              assetAccountId: values.assetAccountId,
              liabilityAccountId: values.liabilityAccountId,
              description: values.description || null,
            };

    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });

    if (response.status === 201) {
      toast.success('Created.');
      refetch();
      const created = (await response.json()) as { id: string };
      const navState =
        pendingAttachments.length > 0 ? { pendingAttachments } : undefined;
      navigate(`/movements/${created.id}/edit?created=1`, {
        replace: true,
        state: navState,
      });
      return { ok: true };
    }

    if (response.status === 422) {
      const envelope = await response.json();
      return { ok: false, errors: parseValidationErrors(envelope) };
    }

    toast.error("Couldn't save. Try again.");
    return { ok: false, errors: { _form: 'Network or server error.' } };
  }

  if (!movementType) return null;

  return (
    <div className="space-y-6">
      <MovementForm
        type={movementType}
        mode="create"
        initialValues={initialValues}
        accounts={narrowedAccounts}
        categories={categories ?? []}
        onSubmit={onSubmit}
        onCancel={() => navigate('/movements')}
      />

      {movementType !== 'LiabilityPayment' && (
        <AttachmentDropzone
          mode="create"
          parentType={movementType as 'Transaction' | 'Transfer'}
          onPendingChange={setPendingAttachments}
          className="mx-auto max-w-3xl"
        />
      )}
    </div>
  );
}
