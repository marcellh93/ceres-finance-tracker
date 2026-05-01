import { useMemo } from 'react';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { toast } from 'sonner';
import { MovementForm, type MovementFormValues } from './MovementForm';
import {
  ACCOUNTS_ACTIVE_URL,
  CATEGORIES_ACTIVE_URL,
  MOVEMENT_TYPE_URL,
  TRANSACTION_BY_ID_URL,
  TRANSFER_BY_ID_URL,
  LIABILITY_PAYMENT_BY_ID_URL,
  type MovementType,
  type MovementTypeDto,
  type TransactionEditDto,
  type TransferEditDto,
  type LiabilityPaymentEditDto,
  type AccountOptionDto,
  type CategoryOptionDto,
} from './movements-api';
import { parseValidationErrors } from './movement-validation';
import { useApi } from '../../lib/use-api';

// ── DTO → form values mappers ──

function transactionDtoToValues(dto: TransactionEditDto): MovementFormValues {
  return {
    date: dto.date,
    amount: String(dto.amount),
    description: dto.description ?? '',
    accountId: dto.accountId,
    categoryId: dto.categoryId,
    sourceAccountId: null,
    destAccountId: null,
    assetAccountId: null,
    liabilityAccountId: null,
    isCleared: dto.isCleared,
    budgetId: null,
    needsReview: false,
  };
}

function transferDtoToValues(dto: TransferEditDto): MovementFormValues {
  return {
    date: dto.date,
    amount: String(dto.amount),
    description: dto.description ?? '',
    accountId: null,
    categoryId: null,
    sourceAccountId: dto.sourceAccountId,
    destAccountId: dto.destAccountId,
    assetAccountId: null,
    liabilityAccountId: null,
    isCleared: dto.isCleared,
    budgetId: null,
    needsReview: false,
  };
}

function liabilityPaymentDtoToValues(dto: LiabilityPaymentEditDto): MovementFormValues {
  return {
    date: dto.date,
    amount: String(dto.amount),
    description: dto.description ?? '',
    accountId: null,
    categoryId: null,
    sourceAccountId: null,
    destAccountId: null,
    assetAccountId: dto.assetAccountId,
    liabilityAccountId: dto.liabilityAccountId,
    isCleared: dto.isCleared,
    budgetId: null,
    needsReview: false,
  };
}

// ── Inner component: runs typed entity fetch after discriminator is resolved ──

function MovementEditInner({
  id,
  movementType,
}: {
  id: string;
  movementType: MovementType;
}) {
  const navigate = useNavigate();
  const { refetch } = useOutletContext<{ refetch: () => void }>();

  const typedUrl =
    movementType === 'Transaction'
      ? TRANSACTION_BY_ID_URL(id)
      : movementType === 'Transfer'
        ? TRANSFER_BY_ID_URL(id)
        : LIABILITY_PAYMENT_BY_ID_URL(id);

  const { data: typedData, loading: typedLoading } = useApi<
    TransactionEditDto | TransferEditDto | LiabilityPaymentEditDto
  >(typedUrl);

  const { data: accounts } = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);
  const { data: categories } = useApi<CategoryOptionDto[]>(CATEGORIES_ACTIVE_URL);

  const initialValues = useMemo<MovementFormValues | null>(() => {
    if (!typedData) return null;
    if (movementType === 'Transaction') {
      return transactionDtoToValues(typedData as TransactionEditDto);
    }
    if (movementType === 'Transfer') {
      return transferDtoToValues(typedData as TransferEditDto);
    }
    return liabilityPaymentDtoToValues(typedData as LiabilityPaymentEditDto);
  }, [typedData, movementType]);

  async function onSubmit(
    values: MovementFormValues,
  ): Promise<{ ok: true } | { ok: false; errors: Record<string, string> }> {
    const url =
      movementType === 'Transaction'
        ? TRANSACTION_BY_ID_URL(id)
        : movementType === 'Transfer'
          ? TRANSFER_BY_ID_URL(id)
          : LIABILITY_PAYMENT_BY_ID_URL(id);

    const body =
      movementType === 'Transaction'
        ? {
            date: values.date,
            amount: Number(values.amount),
            accountId: values.accountId,
            categoryId: values.categoryId,
            description: values.description || null,
            isCleared: values.isCleared,
            budgetId: values.budgetId,
            needsReview: values.needsReview,
          }
        : movementType === 'Transfer'
          ? {
              date: values.date,
              amount: Number(values.amount),
              sourceAccountId: values.sourceAccountId,
              destAccountId: values.destAccountId,
              description: values.description || null,
              isCleared: values.isCleared,
            }
          : {
              date: values.date,
              amount: Number(values.amount),
              assetAccountId: values.assetAccountId,
              liabilityAccountId: values.liabilityAccountId,
              description: values.description || null,
              isCleared: values.isCleared,
            };

    const response = await fetch(url, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });

    if (response.status === 204) {
      toast.success('Saved.');
      refetch();
      navigate('/movements');
      return { ok: true };
    }

    if (response.status === 422) {
      const envelope = await response.json();
      return { ok: false, errors: parseValidationErrors(envelope) };
    }

    toast.error("Couldn't save. Try again.");
    return { ok: false, errors: { _form: 'Network or server error.' } };
  }

  async function onDelete() {
    const url =
      movementType === 'Transaction'
        ? TRANSACTION_BY_ID_URL(id)
        : movementType === 'Transfer'
          ? TRANSFER_BY_ID_URL(id)
          : LIABILITY_PAYMENT_BY_ID_URL(id);

    const response = await fetch(url, { method: 'DELETE' });

    if (response.status === 204) {
      toast.success('Deleted.');
      refetch();
      navigate('/movements');
      return;
    }

    toast.error("Couldn't delete.");
  }

  if (typedLoading || !initialValues) {
    return <div className="text-sm text-muted-foreground">Loading…</div>;
  }

  return (
    <MovementForm
      type={movementType}
      mode="edit"
      initialValues={initialValues}
      accounts={accounts ?? []}
      categories={categories ?? []}
      onSubmit={onSubmit}
      onDelete={onDelete}
      onCancel={() => navigate('/movements')}
    />
  );
}

// ── MovementEdit: resolves discriminator, then delegates ──

export function MovementEdit() {
  const { id } = useParams<{ id: string }>();
  const resolvedId = id ?? '';

  const { data: discriminator, error: discriminatorError, loading: discriminatorLoading } =
    useApi<MovementTypeDto>(MOVEMENT_TYPE_URL(resolvedId));

  if (discriminatorLoading) {
    return <div className="text-sm text-muted-foreground">Loading…</div>;
  }

  if (discriminatorError || !discriminator) {
    return (
      <div className="space-y-4">
        <p className="text-sm text-muted-foreground">Movement not found.</p>
        <Link to="/movements">Back to movements</Link>
      </div>
    );
  }

  return (
    <MovementEditInner id={resolvedId} movementType={discriminator.movementType} />
  );
}
