import { useEffect, useState } from 'react';
import { Link, useNavigate, useOutletContext, useSearchParams } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { toast } from 'sonner';
import { CategoryBudgetForm, type CategoryBudgetFormValues } from './CategoryBudgetForm';
import { GoalBudgetForm, type GoalBudgetFormValues } from './GoalBudgetForm';
import {
  CATEGORY_BUDGETS_URL,
  CATEGORY_BUDGET_REACTIVATE_URL,
  GOAL_BUDGETS_URL,
  type DuplicateBudgetEnvelope,
  type GoalType,
} from './budgets-api';
import { toFormErrors } from '../movements/movement-validation';
import { apiFetch } from '../../lib/api-client';
import { useApi } from '../../lib/use-api';

type CreateType = 'category' | 'spending' | 'savings';

function parseTypeParam(t: string | null): CreateType | null {
  if (t === 'category' || t === 'spending' || t === 'savings') return t;
  return null;
}

type Conflict = {
  existingBudgetId: string;
  existingIsActive: boolean;
};

type LayoutContext = { refetch: () => void };

export function BudgetCreate() {
  useDocumentTitle('New Budget');
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext | null>();
  const typeParam = parseTypeParam(searchParams.get('type'));

  // Bounce back if missing/invalid.
  useEffect(() => {
    if (!typeParam) navigate('/budgets', { replace: true });
  }, [typeParam, navigate]);

  const [conflict, setConflict] = useState<Conflict | null>(null);
  const [currencySymbol, setCurrencySymbol] = useState('');

  const { data: currencies } = useApi<{ id: number; code: string; symbol: string }[]>('/api/currencies');

  if (!typeParam) return null;

  // ---- Category Budget ----
  if (typeParam === 'category') {
    const initialValues: CategoryBudgetFormValues = {
      categoryId: null,
      currencyId: null,
      limitAmount: '',
    };

    async function onSubmit(values: CategoryBudgetFormValues) {
      setConflict(null);
      const response = await apiFetch(CATEGORY_BUDGETS_URL, {
        method: 'POST',
        body: {
          categoryId: values.categoryId,
          currencyId: values.currencyId,
          limitAmount: Number(values.limitAmount),
        },
      });

      if (response.status === 201) {
        toast.success('Created.');
        ctx?.refetch();
        navigate('/budgets?type=category');
        return { ok: true } as const;
      }

      if (!response.ok && response.status === 409) {
        // The DUPLICATE_BUDGET envelope carries fields beyond code/message;
        // apiFetch exposes the raw parsed body for exactly this case.
        const env = ('body' in response ? response.body : null) as DuplicateBudgetEnvelope | null;
        setConflict({
          existingBudgetId: env?.error.existingBudgetId ?? '',
          existingIsActive: env?.error.existingIsActive ?? false,
        });
        // Return an empty errors map. The conflict prompt rendered above the
        // form is the actionable surface — also surfacing the same message in
        // the form's _form banner just stacks two banners saying the same
        // thing. The form's submit button stays enabled so the user can fix
        // the picker and try again, or click "Edit it instead" to bail out.
        return { ok: false, errors: {} } as const;
      }

      if (!response.ok && response.status === 422) {
        return { ok: false, errors: toFormErrors(response) } as const;
      }

      toast.error("Couldn't save. Try again.");
      return { ok: false, errors: { _form: 'Network or server error.' } } as const;
    }

    function onCurrencyChange(currencyId: number | null) {
      setCurrencySymbol(currencies?.find((c) => c.id === currencyId)?.symbol ?? '');
    }

    async function reactivateAndRedirect(id: string) {
      const response = await apiFetch(CATEGORY_BUDGET_REACTIVATE_URL(id), { method: 'PATCH' });
      if (response.status === 204) {
        toast.success('Reactivated.');
        navigate(`/budgets/${id}/edit`);
      } else {
        toast.error("Couldn't reactivate.");
      }
    }

    return (
      <div className="space-y-4">
        <h1 className="text-3xl font-semibold">New category budget</h1>
        {conflict && (
          <div className="rounded-md border border-warning/30 bg-warning/10 p-4 text-sm">
            {conflict.existingIsActive ? (
              <>
                This combination already has an active budget.{' '}
                <Link to={`/budgets/${conflict.existingBudgetId}/edit`} className="underline">
                  Edit it instead
                </Link>
                .
              </>
            ) : (
              <>
                An archived budget for this category and currency exists.{' '}
                <button
                  type="button"
                  className="underline"
                  onClick={() => reactivateAndRedirect(conflict.existingBudgetId)}
                >
                  Reactivate it
                </button>
                .
              </>
            )}
          </div>
        )}
        <CategoryBudgetForm
          mode="create"
          initialValues={initialValues}
          currencySymbol={currencySymbol}
          onCurrencyChange={onCurrencyChange}
          onSubmit={onSubmit}
          onCancel={() => navigate('/budgets?type=category')}
        />
      </div>
    );
  }

  // ---- Goal Budget (Spending or Savings) ----
  const goalType: GoalType = typeParam === 'spending' ? 'Spending' : 'Savings';

  const initialGoalValues: GoalBudgetFormValues = {
    name: '',
    goalType,
    currencyId: null,
    targetAmount: '',
    startDate: new Date().toISOString().slice(0, 10),
    endDate: null,
    description: null,
    linkedAccountId: null,
  };

  async function onGoalSubmit(values: GoalBudgetFormValues) {
    const response = await apiFetch(GOAL_BUDGETS_URL, {
      method: 'POST',
      body: {
        name: values.name,
        goalType: values.goalType,
        currencyId: values.currencyId,
        targetAmount: Number(values.targetAmount),
        startDate: values.startDate,
        endDate: values.endDate,
        description: values.description,
        linkedAccountId: values.linkedAccountId,
      },
    });

    if (response.ok && response.status === 201) {
      toast.success('Created.');
      ctx?.refetch();
      navigate('/budgets?type=goal');
      return { ok: true } as const;
    }

    if (!response.ok && response.status === 422) {
      return { ok: false, errors: toFormErrors(response) } as const;
    }

    toast.error("Couldn't save. Try again.");
    return { ok: false, errors: { _form: 'Network or server error.' } } as const;
  }

  return (
    <div className="space-y-4">
      <h1 className="text-3xl font-semibold">
        New {goalType.toLowerCase()} goal
      </h1>
      <GoalBudgetForm
        mode="create"
        goalType={goalType}
        initialValues={initialGoalValues}
        onSubmit={onGoalSubmit}
        onCancel={() => navigate('/budgets?type=goal')}
      />
    </div>
  );
}
