import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { toast } from 'sonner';
import { CategoryBudgetForm, type CategoryBudgetFormValues } from './CategoryBudgetForm';
import { GoalBudgetForm, type GoalBudgetFormValues } from './GoalBudgetForm';
import {
  BUDGET_DISCRIMINATOR_URL,
  CATEGORY_BUDGET_BY_ID_URL,
  GOAL_BUDGET_BY_ID_URL,
  type BudgetDiscriminatorDto,
  type CategoryBudgetEditDto,
  type GoalBudgetEditDto,
} from './budgets-api';
import { parseValidationErrors } from '../movements/movement-validation';
import { useApi } from '../../lib/use-api';

export function BudgetEdit() {
  useDocumentTitle('Edit Budget');
  const { id } = useParams<{ id: string }>();
  const resolvedId = id ?? '';

  const { data: discriminator, error: discErr, loading: discLoading } =
    useApi<BudgetDiscriminatorDto>(BUDGET_DISCRIMINATOR_URL(resolvedId));

  if (discLoading) {
    return <div className="text-sm text-muted-foreground">Loading…</div>;
  }
  if (discErr || !discriminator) {
    return (
      <div className="space-y-4">
        <p className="text-sm text-muted-foreground">Budget not found.</p>
      </div>
    );
  }

  if (discriminator.kind === 'CategoryBudget') {
    return <CategoryEdit id={resolvedId} />;
  }
  return <GoalEdit id={resolvedId} />;
}

function CategoryEdit({ id }: { id: string }) {
  const navigate = useNavigate();
  const [currencySymbol, setCurrencySymbol] = useState('');

  const { data: dto, loading } = useApi<CategoryBudgetEditDto>(CATEGORY_BUDGET_BY_ID_URL(id));
  const { data: currencies } = useApi<{ id: number; code: string; symbol: string }[]>('/api/currencies');

  useEffect(() => {
    if (!dto || !currencies) return;
    setCurrencySymbol(currencies.find((c) => c.id === dto.currencyId)?.symbol ?? '');
  }, [dto, currencies]);

  if (loading || !dto) return <div className="text-sm text-muted-foreground">Loading…</div>;

  const initialValues: CategoryBudgetFormValues = {
    categoryId: dto.categoryId,
    currencyId: dto.currencyId,
    limitAmount: String(dto.limitAmount),
  };

  async function onSubmit(values: CategoryBudgetFormValues) {
    const response = await fetch(CATEGORY_BUDGET_BY_ID_URL(id), {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        categoryId: values.categoryId,
        currencyId: values.currencyId,
        limitAmount: Number(values.limitAmount),
        isActive: dto?.isActive ?? true,
      }),
    });

    if (response.status === 204) {
      toast.success('Saved.');
      navigate('/budgets?type=category');
      return { ok: true } as const;
    }
    if (response.status === 422) {
      const envelope = await response.json();
      return { ok: false, errors: parseValidationErrors(envelope) } as const;
    }
    toast.error("Couldn't save.");
    return { ok: false, errors: { _form: 'Network or server error.' } } as const;
  }

  function onCurrencyChange(currencyId: number | null) {
    setCurrencySymbol(currencies?.find((c) => c.id === currencyId)?.symbol ?? '');
  }

  return (
    <div className="space-y-4">
      <h1 className="text-3xl font-semibold">Edit category budget</h1>
      <CategoryBudgetForm
        mode="edit"
        initialValues={initialValues}
        currencySymbol={currencySymbol}
        onCurrencyChange={onCurrencyChange}
        onSubmit={onSubmit}
        onCancel={() => navigate('/budgets?type=category')}
      />
    </div>
  );
}

function GoalEdit({ id }: { id: string }) {
  const navigate = useNavigate();
  const { data: dto, loading } = useApi<GoalBudgetEditDto>(GOAL_BUDGET_BY_ID_URL(id));

  if (loading || !dto) return <div className="text-sm text-muted-foreground">Loading…</div>;

  const initialValues: GoalBudgetFormValues = {
    name: dto.name,
    goalType: dto.goalType,
    currencyId: dto.currencyId,
    targetAmount: String(dto.targetAmount),
    startDate: dto.startDate,
    endDate: dto.endDate,
    description: dto.description,
    linkedAccountId: dto.linkedAccountId,
  };

  async function onSubmit(values: GoalBudgetFormValues) {
    const response = await fetch(GOAL_BUDGET_BY_ID_URL(id), {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: values.name,
        goalType: values.goalType,
        currencyId: values.currencyId,
        targetAmount: Number(values.targetAmount),
        startDate: values.startDate,
        endDate: values.endDate,
        description: values.description,
        linkedAccountId: values.linkedAccountId,
        isActive: dto?.isActive ?? true,
      }),
    });

    if (response.status === 204) {
      toast.success('Saved.');
      navigate('/budgets?type=goal');
      return { ok: true } as const;
    }
    if (response.status === 422) {
      const envelope = await response.json();
      return { ok: false, errors: parseValidationErrors(envelope) } as const;
    }
    toast.error("Couldn't save.");
    return { ok: false, errors: { _form: 'Network or server error.' } } as const;
  }

  return (
    <div className="space-y-4">
      <h1 className="text-3xl font-semibold">
        Edit {dto.goalType.toLowerCase()} goal
      </h1>
      <GoalBudgetForm
        mode="edit"
        goalType={dto.goalType}
        initialValues={initialValues}
        onSubmit={onSubmit}
        onCancel={() => navigate('/budgets?type=goal')}
      />
    </div>
  );
}
