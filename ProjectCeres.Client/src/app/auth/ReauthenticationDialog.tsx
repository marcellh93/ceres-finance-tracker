import { useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useAuth } from './auth-context';
import { getCachedXsrfRequestToken, setCachedXsrfRequestToken } from './csrf';

async function ensureCsrf(): Promise<void> {
  if (getCachedXsrfRequestToken()) return;
  const response = await fetch('/api/auth/csrf', { method: 'GET', credentials: 'include' });
  const token = response.headers.get('X-XSRF-TOKEN');
  if (token) setCachedXsrfRequestToken(token);
}

interface ReauthenticationDialogProps {
  open: boolean;
  onSuccess: () => void;
  onCancel: () => void;
}

export function ReauthenticationDialog({ open, onSuccess, onCancel }: ReauthenticationDialogProps) {
  const { user } = useAuth();
  const isMfa = user?.twoFactorEnabled ?? false;

  const [value, setValue] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Reset entry + error on cancel so a re-open starts clean.
  const handleCancel = () => {
    setValue('');
    setErrorMessage(null);
    onCancel();
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      await ensureCsrf();
      const xsrf = getCachedXsrfRequestToken();
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      if (xsrf) headers['X-XSRF-TOKEN'] = xsrf;

      const body = isMfa ? { totpCode: value } : { password: value };

      const response = await fetch('/api/auth/reauth', {
        method: 'POST',
        credentials: 'include',
        headers,
        body: JSON.stringify(body),
      });

      if (response.status === 204) {
        setValue('');
        onSuccess();
        return;
      }

      if (response.status === 401 || response.status === 422) {
        const payload = (await response.json().catch(() => null)) as {
          error?: { code?: string; message?: string; details?: Array<{ field: string; message: string }> };
        } | null;

        if (response.status === 422 && payload?.error?.details?.length) {
          setErrorMessage(payload.error.details[0].message);
        } else {
          setErrorMessage(payload?.error?.message ?? 'Authentication failed. Please try again.');
        }
        return;
      }

      if (response.status === 429) {
        setErrorMessage('Too many attempts. Please wait and try again.');
        return;
      }

      setErrorMessage('Something went wrong. Please try again.');
    } catch {
      setErrorMessage('Something went wrong. Please try again.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={(isOpen) => { if (!isOpen) handleCancel(); }}>
      <DialogContent showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>Confirm your identity</DialogTitle>
          <DialogDescription>
            {isMfa
              ? 'Enter your authenticator code to continue.'
              : 'Enter your password to continue.'}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4" noValidate>
          {isMfa ? (
            <div className="space-y-1.5">
              <Label htmlFor="reauth-totp-code">Authenticator code</Label>
              <Input
                id="reauth-totp-code"
                inputMode="numeric"
                autoComplete="one-time-code"
                autoFocus
                value={value}
                onChange={(e) => setValue(e.target.value)}
              />
            </div>
          ) : (
            <div className="space-y-1.5">
              <Label htmlFor="reauth-password">Password</Label>
              <Input
                id="reauth-password"
                type="password"
                autoComplete="current-password"
                autoFocus
                value={value}
                onChange={(e) => setValue(e.target.value)}
              />
            </div>
          )}

          {errorMessage && (
            <div role="alert" aria-live="assertive" className="text-sm text-destructive">
              {errorMessage}
            </div>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={handleCancel} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Confirming…' : 'Confirm'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
