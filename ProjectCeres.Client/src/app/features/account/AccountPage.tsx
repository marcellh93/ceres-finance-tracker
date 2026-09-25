import { useState } from 'react';
import { Download } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { useDocumentTitle } from '../../lib/use-document-title';
import { useStepUp, ReauthCancelledError } from '../../auth/use-step-up';
import { requestDataExport } from './account-api';

/**
 * Account self-service surface (`/settings/account`). Stage 13.8 ships the data
 * export action; Stage 13.9 adds the erasure section here. Reauth-gated actions
 * go through `requireStepUp`, which opens the password dialog on 401 and replays.
 */
export function AccountPage() {
  useDocumentTitle('Account');
  const { requireStepUp } = useStepUp();
  const [busy, setBusy] = useState(false);

  const onExport = async () => {
    setBusy(true);
    try {
      const result = await requireStepUp(() => requestDataExport());

      if (result.ok) {
        toast.success("Export started — we'll email you a download link when it's ready.");
        return;
      }
      if (result.status === 429) {
        toast.error('You can request one export per day. Check your email for your most recent link.');
        return;
      }
      toast.error('Could not start your export. Please try again.');
    } catch (err) {
      // Cancelling the reauth dialog is a deliberate choice — no toast.
      if (err instanceof ReauthCancelledError) return;
      toast.error('Could not start your export. Please try again.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Account</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          Manage your account and your personal data.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base font-medium">Your data</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <p className="text-muted-foreground text-sm">
            Download a copy of all your data — accounts, transactions, categories,
            budgets, and attachments. We&apos;ll email you a secure link when it&apos;s ready.
          </p>
          <Button onClick={() => void onExport()} disabled={busy}>
            <Download aria-hidden="true" />
            {busy ? 'Starting…' : 'Export my data'}
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
