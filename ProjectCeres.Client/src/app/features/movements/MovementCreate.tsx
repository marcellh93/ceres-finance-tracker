import { useMemo } from 'react';
import { useNavigate, useOutletContext, useSearchParams } from 'react-router-dom';
import { toast } from 'sonner';
import { ArrowLeftRight, CreditCard, Receipt } from 'lucide-react';
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

// ── URL param helpers ──

function urlToMovementType(t: string | null): MovementType | null {
  if (t === 'transaction') return 'Transaction';
  if (t === 'transfer') return 'Transfer';
  if (t === 'liabilitypayment') return 'LiabilityPayment';
  return null;
}

// ── Type Picker ──

type PickerCard = {
  urlValue: string;
  label: string;
  icon: React.ReactNode;
};

function TypePicker({ onSelect }: { onSelect: (urlValue: string) => void }) {
  const cards: PickerCard[] = [
    { urlValue: 'transaction', label: 'Transaction', icon: <Receipt className="h-6 w-6" /> },
    { urlValue: 'transfer', label: 'Transfer', icon: <ArrowLeftRight className="h-6 w-6" /> },
    { urlValue: 'liabilitypayment', label: 'Liability Payment', icon: <CreditCard className="h-6 w-6" /> },
  ];

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold">What would you like to add?</h2>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        {cards.map((card) => (
          <button
            key={card.urlValue}
            type="button"
            onClick={() => onSelect(card.urlValue)}
            className="flex flex-col items-center gap-2 rounded-lg border border-border bg-card p-6 text-card-foreground shadow-sm transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            {card.icon}
            <span className="font-medium">{card.label}</span>
          </button>
        ))}
      </div>
    </div>
  );
}

// ── MovementCreate ──

export function MovementCreate() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const { refetch } = useOutletContext<{ refetch: () => void }>();

  const typeParam = searchParams.get('type');
  const movementType = urlToMovementType(typeParam);

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

  function handlePickType(urlValue: string) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('type', urlValue);
      return next;
    });
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
      const created = (await response.json()) as { id: string };
      refetch();
      navigate(`/movements/${created.id}/edit?created=1`, { replace: true });
      return { ok: true };
    }

    if (response.status === 422) {
      const envelope = await response.json();
      return { ok: false, errors: parseValidationErrors(envelope) };
    }

    toast.error("Couldn't save. Try again.");
    return { ok: false, errors: { _form: 'Network or server error.' } };
  }

  // If no type selected, show the picker
  if (!movementType) {
    return <TypePicker onSelect={handlePickType} />;
  }

  return (
    <MovementForm
      type={movementType}
      mode="create"
      initialValues={initialValues}
      accounts={accounts ?? []}
      categories={categories ?? []}
      onSubmit={onSubmit}
      onCancel={() => navigate('/movements')}
    />
  );
}
