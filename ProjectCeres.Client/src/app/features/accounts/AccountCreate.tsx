import { toast } from 'sonner';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { apiFetch } from '../../lib/api-client';
import { AccountForm } from './AccountForm';
import {
  ACCOUNTS_URL,
  ACCOUNT_TYPES_URL,
  CURRENCIES_URL,
  type AccountFormValues,
  type AccountTypeDto,
  type CreateAccountRequest,
  type CurrencyDto,
  type UpdateAccountRequest,
} from './accounts-api';

type LayoutContext = { refetch: () => void };

const initialValues: AccountFormValues = {
  name: '',
  accountTypeId: 1,
  currencyId: 1,
  description: '',
  openingBalance: 0,
  openingBalanceDate: new Date().toISOString().slice(0, 10),
  liabilityRepaymentType: null,
  interestRate: null,
  excludeFromSpendable: false,
};

export function AccountCreate() {
  useDocumentTitle('New Account');
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext>();
  const types = useApi<AccountTypeDto[]>(ACCOUNT_TYPES_URL);
  const currencies = useApi<CurrencyDto[]>(CURRENCIES_URL);

  if (types.loading || currencies.loading) {
    return (
      <Card>
        <CardHeader><CardTitle>New account</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </CardContent>
      </Card>
    );
  }

  if (types.error || !types.data || currencies.error || !currencies.data) {
    return (
      <Card>
        <CardHeader><CardTitle>New account</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            Couldn't load account form data.
          </div>
        </CardContent>
      </Card>
    );
  }

  async function handleSubmit(body: CreateAccountRequest | UpdateAccountRequest) {
    try {
      const response = await apiFetch(ACCOUNTS_URL, {
        method: 'POST',
        body,
      });
      if (!response.ok) {
        toast.error("Couldn't save. Try again.");
        return { ok: false as const };
      }
      toast.success('Created.');
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
      <CardHeader><CardTitle>New account</CardTitle></CardHeader>
      <CardContent>
        <AccountForm
          mode="create"
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
