import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useOutletContext, useSearchParams } from 'react-router-dom';
import { toast } from 'sonner';
import { Paperclip } from 'lucide-react';
import { MovementForm, type MovementFormValues } from './MovementForm';
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
import { Button } from '@/components/ui/button';

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

  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [pickedFile, setPickedFile] = useState<File | null>(null);

  // No type in URL → bounce back to the list. The `+ New` dropdown is the only
  // entry point and it always sets ?type=…; a bare /movements/new visit is invalid.
  useEffect(() => {
    if (!movementType) navigate('/movements', { replace: true });
  }, [movementType, navigate]);

  const { data: accounts } = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);
  const { data: categories } = useApi<CategoryOptionDto[]>(CATEGORIES_ACTIVE_URL);

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

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    setPickedFile(file);
  }

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
      const navState = pickedFile ? { pendingAttachment: pickedFile } : undefined;
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
    <div className="space-y-4">
      {movementType !== 'LiabilityPayment' && (
        <div className="mx-auto w-full max-w-2xl rounded-lg border border-border bg-card p-4">
          <div className="flex items-center justify-between gap-3">
            <div className="space-y-0.5">
              <p className="text-sm font-medium">Receipt</p>
              <p className="text-xs text-muted-foreground">
                We'll upload it right after we save the movement.
              </p>
            </div>
            <div className="flex items-center gap-3">
              {pickedFile && (
                <span className="text-sm text-muted-foreground" data-testid="picked-filename">
                  {pickedFile.name}
                </span>
              )}
              <Button
                type="button"
                variant="outline"
                onClick={() => fileInputRef.current?.click()}
              >
                <Paperclip className="mr-2 h-4 w-4" />
                Attach receipt
              </Button>
              <input
                ref={fileInputRef}
                type="file"
                className="hidden"
                onChange={handleFileChange}
              />
            </div>
          </div>
        </div>
      )}

      <MovementForm
        type={movementType}
        mode="create"
        initialValues={initialValues}
        accounts={accounts ?? []}
        categories={categories ?? []}
        onSubmit={onSubmit}
        onCancel={() => navigate('/movements')}
      />
    </div>
  );
}
