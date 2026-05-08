import { toast } from 'sonner';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { AccountForm } from './AccountForm';
import {
  ACCOUNT_BY_ID_URL,
  ACCOUNT_TYPES_URL,
  CURRENCIES_URL,
  type AccountDetailDto,
  type AccountFormValues,
  type AccountTypeDto,
  type CreateAccountRequest,
  type CurrencyDto,
  type UpdateAccountRequest,
} from './accounts-api';

type LayoutContext = { refetch: () => void };

export function AccountEdit() {
  useDocumentTitle('Edit Account');
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext>();

  const detail = useApi<AccountDetailDto>(id ? ACCOUNT_BY_ID_URL(id) : '/api/accounts/__missing__');
  const types = useApi<AccountTypeDto[]>(ACCOUNT_TYPES_URL);
  const currencies = useApi<CurrencyDto[]>(CURRENCIES_URL);

  if (detail.loading || types.loading || currencies.loading) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit account</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </CardContent>
      </Card>
    );
  }

  if (detail.error || !detail.data) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit account</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            That account doesn't exist.
          </div>
          <Button variant="outline" nativeButton={false} className="mt-4" render={<Link to="/accounts">Back to Accounts</Link>} />
        </CardContent>
      </Card>
    );
  }

  if (types.error || !types.data || currencies.error || !currencies.data) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit account</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            Couldn't load account form data.
          </div>
        </CardContent>
      </Card>
    );
  }

  const initialValues: AccountFormValues = {
    name: detail.data.name,
    accountTypeId: detail.data.accountTypeId,
    currencyId: detail.data.currencyId,
    description: detail.data.description ?? '',
    openingBalance: detail.data.openingBalance,
    openingBalanceDate: detail.data.openingBalanceDate ?? new Date().toISOString().slice(0, 10),
    liabilityRepaymentType: detail.data.liabilityRepaymentType,
    interestRate: detail.data.interestRate,
    excludeFromSpendable: detail.data.excludeFromSpendable,
  };

  async function handleSubmit(body: CreateAccountRequest | UpdateAccountRequest) {
    try {
      const response = await fetch(ACCOUNT_BY_ID_URL(id!), {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        toast.error("Couldn't save. Try again.");
        return { ok: false as const };
      }
      toast.success('Saved.');
      ctx?.refetch();
      navigate('/accounts');
      return { ok: true as const };
    } catch {
      toast.error("Couldn't save. Try again.");
      return { ok: false as const };
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle>Edit account</CardTitle></CardHeader>
      <CardContent>
        <AccountForm
          mode="edit"
          initialValues={initialValues}
          accountTypes={types.data}
          currencies={currencies.data}
          onSubmit={handleSubmit}
          onCancel={() => navigate('/accounts')}
        />
      </CardContent>
    </Card>
  );
}
