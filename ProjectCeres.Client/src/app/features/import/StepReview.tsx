import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import {
  ACCOUNTS_ACTIVE_URL,
  type AccountOptionDto,
} from '../movements/movements-api';
import type { ImportColumnMappings } from './import-api';

type Props = {
  fileName: string;
  accountId: string;
  mappings: ImportColumnMappings;
  submitting: boolean;
  onSubmit: () => void;
  onBack: () => void;
};

export function StepReview({ fileName, accountId, mappings, submitting, onSubmit, onBack }: Props) {
  const accounts = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);
  const account = accounts.data?.find((a) => a.id === accountId) ?? null;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Review and import</CardTitle>
      </CardHeader>
      <CardContent className="space-y-6">
        <dl className="space-y-3 text-sm">
          <Row label="File">{fileName}</Row>
          <Row label="Account">
            {accounts.loading ? <Skeleton className="h-4 w-32 inline-block" />
              : account ? account.name
              : <span className="text-muted-foreground italic">unknown</span>}
          </Row>
          <Row label="Date column">{mappings.dateColumn}</Row>
          <Row label="Amount column">{mappings.amountColumn}</Row>
          <Row label="Description column">{mappings.descriptionColumn}</Row>
          {mappings.categoryColumn ? (
            <Row label="Category column">{mappings.categoryColumn}</Row>
          ) : null}
        </dl>

        <div className="flex items-center gap-2 pt-2">
          <Button type="button" disabled={submitting} onClick={onSubmit}>
            {submitting ? 'Importing…' : 'Import'}
          </Button>
          <Button type="button" variant="outline" disabled={submitting} onClick={onBack}>
            Back
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-wrap gap-2">
      <dt className="w-44 shrink-0 font-medium text-muted-foreground">{label}</dt>
      <dd className="text-foreground">{children}</dd>
    </div>
  );
}
