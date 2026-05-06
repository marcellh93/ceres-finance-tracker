import { useState } from 'react';
import { toast } from 'sonner';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { CardError } from '../../components/CardError';
import { AccountCombobox } from '../../components/AccountCombobox';
import { useApi } from '../../lib/use-api';
import {
  ACCOUNTS_ACTIVE_URL,
  type AccountOptionDto,
} from '../movements/movements-api';
import { FileDropzone } from './FileDropzone';
import {
  IMPORT_HEADERS_URL,
  type HeaderDetectionResult,
} from './import-api';

const MAX_IMPORT_BYTES = 10 * 1024 * 1024;
const ACCEPT_EXTS = '.csv,.xlsx';

type Props = {
  file: File | null;
  accountId: string | null;
  onFileChange: (file: File) => void;
  onAccountChange: (accountId: string) => void;
  onContinue: (headers: HeaderDetectionResult) => void;
};

export function StepFile({ file, accountId, onFileChange, onAccountChange, onContinue }: Props) {
  const accounts = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);
  const [submitting, setSubmitting] = useState(false);

  const canContinue = file !== null && accountId !== null && !submitting;

  async function handleContinue() {
    if (!file || !accountId) return;
    setSubmitting(true);

    const fd = new FormData();
    fd.append('file', file);

    try {
      const response = await fetch(IMPORT_HEADERS_URL, { method: 'POST', body: fd });
      if (response.ok) {
        const headers = (await response.json()) as HeaderDetectionResult;
        onContinue(headers);
      } else {
        toast.warning("We couldn't read the file's headers. You can map columns manually.");
        onContinue({
          headers: [],
          dateColumn: null,
          amountColumn: null,
          descriptionColumn: null,
          categoryColumn: null,
        });
      }
    } catch {
      toast.warning("We couldn't read the file's headers. You can map columns manually.");
      onContinue({
        headers: [],
        dateColumn: null,
        amountColumn: null,
        descriptionColumn: null,
        categoryColumn: null,
      });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Choose a file and account</CardTitle>
      </CardHeader>
      <CardContent className="space-y-6">
        <div className="space-y-1.5">
          <Label htmlFor="import-file">Bank statement file</Label>
          <FileDropzone
            inputId="import-file"
            accept={ACCEPT_EXTS}
            maxBytes={MAX_IMPORT_BYTES}
            selectedFile={file}
            onFile={onFileChange}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="import-account">Destination account</Label>
          {accounts.loading ? (
            <Skeleton className="h-9 w-full" />
          ) : accounts.error || !accounts.data ? (
            <CardError section="accounts" onRetry={accounts.refetch} />
          ) : (
            <AccountCombobox
              accounts={accounts.data}
              value={accountId}
              onChange={onAccountChange}
              placeholder="Select an account"
            />
          )}
        </div>

        <div className="flex items-center gap-2 pt-2">
          <Button type="button" disabled={!canContinue} onClick={handleContinue}>
            {submitting ? 'Loading…' : 'Continue'}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
