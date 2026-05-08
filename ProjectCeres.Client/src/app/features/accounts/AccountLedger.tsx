import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { ArrowLeft } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { cn } from '@/lib/utils';
import {
  ACCOUNT_BY_ID_URL,
  ACCOUNT_LEDGER_URL,
  type AccountDetailDto,
  type AccountLedgerDto,
} from './accounts-api';
import { project, type ProjectionResult } from './projection';

export function AccountLedger() {
  useDocumentTitle('Account Ledger');
  const { id } = useParams<{ id: string }>();
  const account = useApi<AccountDetailDto>(id ? ACCOUNT_BY_ID_URL(id) : '/api/accounts/__missing__');
  const ledger = useApi<AccountLedgerDto>(id ? ACCOUNT_LEDGER_URL(id) : '/api/accounts/__missing__/ledger');

  if (account.loading || ledger.loading) {
    return (
      <div className="mx-auto max-w-4xl space-y-6">
        <Skeleton className="h-9 w-64" />
        <Card>
          <CardContent className="space-y-2 py-6">
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-full" />
          </CardContent>
        </Card>
      </div>
    );
  }

  if (account.error || !account.data) {
    return (
      <div className="mx-auto max-w-4xl space-y-6">
        <Card>
          <CardContent className="py-6 space-y-4">
            <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
              That account doesn't exist.
            </div>
            <Button variant="outline" nativeButton={false} render={<Link to="/accounts">Back to Accounts</Link>} />
          </CardContent>
        </Card>
      </div>
    );
  }

  if (ledger.error || !ledger.data) {
    return (
      <div className="mx-auto max-w-4xl space-y-6">
        <CardError section="ledger" onRetry={ledger.refetch} />
      </div>
    );
  }

  const accountData = account.data;
  const isLiability = accountData.accountTypeName === 'Liability';
  const showProjection =
    isLiability && accountData.liabilityRepaymentType === 'Amortising' && accountData.interestRate != null;
  const currentBalance = ledger.data.entries.length > 0
    ? ledger.data.entries[ledger.data.entries.length - 1].runningBalance
    : accountData.openingBalance;

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <Link to="/accounts" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="h-4 w-4" />
          Back to Accounts
        </Link>
      </div>

      <header>
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-semibold">{accountData.name} — Ledger</h1>
          {!accountData.isActive ? <Badge variant="secondary">Archived</Badge> : null}
        </div>
        <p className="mt-2 text-muted-foreground">
          Every entry that contributes to this account's balance, in chronological order. The final
          running balance matches the account balance.
        </p>
      </header>

      {showProjection ? (
        <PayoffProjectionCard
          balance={Math.abs(currentBalance)}
          annualRate={accountData.interestRate!}
          symbol={accountData.currencySymbol}
        />
      ) : null}

      <Card>
        <CardContent>
          {ledger.data.entries.length === 0 ? (
            <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
              No entries found for this account.
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-28">Date</TableHead>
                  <TableHead>Description</TableHead>
                  <TableHead className="w-40">Category</TableHead>
                  <TableHead className="w-32 text-right">Amount</TableHead>
                  <TableHead className="w-32 text-right">Running</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {ledger.data.entries.map((e, idx) => (
                  <TableRow key={`${e.date}-${idx}-${e.entryType}`}>
                    <TableCell className="text-sm">{formatDate(e.date)}</TableCell>
                    <TableCell className="text-sm">{e.description}</TableCell>
                    <TableCell className="text-sm text-muted-foreground">{e.categoryName ?? '—'}</TableCell>
                    <TableCell
                      className={cn(
                        'text-right tabular-nums text-sm',
                        e.signedAmount < 0 && 'text-destructive',
                      )}
                    >
                      {formatSigned(e.signedAmount, accountData.currencySymbol)}
                    </TableCell>
                    <TableCell
                      className={cn(
                        'text-right tabular-nums text-sm',
                        isLiability && 'text-destructive',
                      )}
                    >
                      {formatBalance(e.runningBalance, accountData.currencySymbol)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function PayoffProjectionCard({
  balance, annualRate, symbol,
}: { balance: number; annualRate: number; symbol: string }) {
  const [paymentInput, setPaymentInput] = useState('');
  const [result, setResult] = useState<ProjectionResult | null>(null);

  function handleCalculate() {
    const payment = Number(paymentInput);
    if (Number.isNaN(payment)) {
      setResult({ ok: false, error: 'Enter a valid number.' });
      return;
    }
    setResult(project(balance, annualRate, payment));
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Payoff projection</CardTitle>
      </CardHeader>
      <CardContent>
        <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1 text-sm">
          <dt className="text-muted-foreground">Outstanding balance</dt>
          <dd className="tabular-nums">{symbol}{balance.toFixed(2)}</dd>
          <dt className="text-muted-foreground">Annual interest rate</dt>
          <dd className="tabular-nums">{(annualRate * 100).toFixed(2)}%</dd>
        </dl>

        <div className="mt-6 flex items-end gap-3">
          <div className="flex-1 space-y-1.5">
            <Label htmlFor="monthlyPayment">Monthly payment</Label>
            <Input
              id="monthlyPayment"
              aria-label="Monthly payment"
              type="number"
              step="0.01"
              min={0}
              value={paymentInput}
              onChange={(e) => setPaymentInput(e.target.value)}
            />
          </div>
          <Button type="button" onClick={handleCalculate}>Calculate</Button>
        </div>

        {result?.ok ? (
          <dl className="mt-6 grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1 text-sm">
            <dt className="text-muted-foreground">Estimated payoff</dt>
            <dd>{result.payoffDate.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })} ({result.monthsToPayoff} months)</dd>
            <dt className="text-muted-foreground">Total interest cost</dt>
            <dd className="tabular-nums">{symbol}{result.totalInterest.toFixed(2)}</dd>
            <dt className="text-muted-foreground">Total paid</dt>
            <dd className="tabular-nums">{symbol}{result.totalPaid.toFixed(2)}</dd>
          </dl>
        ) : null}
        {result && !result.ok ? (
          <p className="mt-4 text-sm text-destructive">{result.error}</p>
        ) : null}
      </CardContent>
    </Card>
  );
}

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString(undefined, { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function formatSigned(amount: number, symbol: string): string {
  const formatted = new Intl.NumberFormat(undefined, {
    minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).format(Math.abs(amount));
  const sign = amount < 0 ? '−' : '+';
  return `${sign}${symbol}${formatted}`;
}

function formatBalance(amount: number, symbol: string): string {
  const formatted = new Intl.NumberFormat(undefined, {
    minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).format(Math.abs(amount));
  const sign = amount < 0 ? '-' : '';
  return `${sign}${symbol}${formatted}`;
}
