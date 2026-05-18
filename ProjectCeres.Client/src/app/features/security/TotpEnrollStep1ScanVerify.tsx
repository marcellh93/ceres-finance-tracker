import { useEffect, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useTranslation } from 'react-i18next';
import { QRCodeSVG } from 'qrcode.react';
import { toast } from 'sonner';
import { ChevronDown, Copy } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InputOTP, InputOTPGroup, InputOTPSlot } from '@/components/ui/input-otp';
import { apiFetch, ReauthRequiredError } from '../../lib/api-client';
import {
  totpCodeSchema,
  type TotpCodeFormValues,
} from '../../auth/schemas/login-totp.schema';

type Props = {
  otpAuthUri: string;
  manualEntryKey: string;
  onEnrolled: (backupCodes: string[]) => void;
  onReauthRequired: () => void;
  onRestart: () => void;
};

type SubmitOutcome =
  | { kind: 'ok'; backupCodes: string[] }
  | { kind: 'invalidCode' }
  | { kind: 'noEnrollment' }
  | { kind: 'reauth' }
  | { kind: 'network' };

async function submitVerify(code: string): Promise<SubmitOutcome> {
  try {
    const result = await apiFetch<{ backupCodes: string[] }>('/api/auth/mfa/enroll/verify', {
      method: 'POST',
      body: { code },
    });
    if (result.ok && result.data?.backupCodes) {
      return { kind: 'ok', backupCodes: result.data.backupCodes };
    }
    if (!result.ok) {
      if (result.code === 'INVALID_MFA_CODE') return { kind: 'invalidCode' };
      if (result.code === 'NO_ENROLLMENT_IN_PROGRESS') return { kind: 'noEnrollment' };
    }
    return { kind: 'invalidCode' };
  } catch (err) {
    if (err instanceof ReauthRequiredError) return { kind: 'reauth' };
    return { kind: 'network' };
  }
}

export function TotpEnrollStep1ScanVerify({
  otpAuthUri,
  manualEntryKey,
  onEnrolled,
  onReauthRequired,
  onRestart,
}: Props) {
  const { t } = useTranslation();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [restartRequired, setRestartRequired] = useState(false);
  const [manualOpen, setManualOpen] = useState(false);
  const submitInFlightRef = useRef(false);

  const form = useForm<TotpCodeFormValues>({
    resolver: zodResolver(totpCodeSchema),
    defaultValues: { code: '' },
    mode: 'onSubmit',
  });

  const codeValue = form.watch('code');

  const onSubmit = async (values: TotpCodeFormValues) => {
    if (submitInFlightRef.current) return;
    submitInFlightRef.current = true;
    setErrorMessage(null);
    try {
      const outcome = await submitVerify(values.code);
      if (outcome.kind === 'ok') {
        onEnrolled(outcome.backupCodes);
        return;
      }
      if (outcome.kind === 'reauth') {
        onReauthRequired();
        return;
      }
      if (outcome.kind === 'noEnrollment') {
        setRestartRequired(true);
        setErrorMessage(t('security.totp.wizard.step1.errors.noEnrollmentInProgress'));
        return;
      }
      if (outcome.kind === 'network') {
        setErrorMessage(t('security.totp.wizard.step1.errors.network'));
        return;
      }
      setErrorMessage(t('security.totp.wizard.step1.errors.invalidCode'));
      form.reset({ code: '' });
    } finally {
      submitInFlightRef.current = false;
    }
  };

  useEffect(() => {
    if (codeValue.length === 6 && /^\d{6}$/.test(codeValue) && !submitInFlightRef.current) {
      void form.handleSubmit(onSubmit)();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [codeValue]);

  const copyManualKey = async () => {
    try {
      await navigator.clipboard.writeText(manualEntryKey.replace(/\s+/g, ''));
      toast.success(t('security.totp.wizard.step1.manualEntryCopied'));
    } catch {
      /* silent — clipboard permission denied; user can still read the key on screen */
    }
  };

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <div className="grid gap-6 sm:grid-cols-[auto_1fr] sm:items-start">
        <div className="space-y-3">
          {/*
            QR codes MUST render dark-on-light with a white quiet zone, in
            BOTH themes — authenticator apps (Google Authenticator, Aegis,
            Authy) scan via the camera as a black-on-white image and refuse
            inverted codes. Two failure modes we hit pre-fix:
              (a) The wrapper used `bg-card`, which is dark in dark mode →
                  the QR's white background looked like a tiny white square
                  in a dark frame with no quiet zone.
              (b) qrcode.react's default `marginSize=0` produces no internal
                  quiet zone, so the QR itself starts at the SVG edge.
            Fix:
              - Force the wrapper to a hard `bg-white` regardless of theme.
              - Set `marginSize={4}` on the SVG so the spec-required
                4-module quiet zone lives INSIDE the SVG, independent of
                whatever pixels surround it.
              - Explicit fgColor/bgColor for resilience against future
                qrcode.react default changes.
          */}
          <div
            className="inline-block rounded-md border border-border bg-white p-3"
            data-testid="totp-qr-wrapper"
          >
            <QRCodeSVG
              value={otpAuthUri}
              size={192}
              level="M"
              marginSize={4}
              fgColor="#000000"
              bgColor="#FFFFFF"
              aria-label="TOTP enrolment QR code"
            />
          </div>

          <details
            className="text-sm"
            open={manualOpen}
            onToggle={(e) => setManualOpen((e.target as HTMLDetailsElement).open)}
          >
            <summary className="flex cursor-pointer items-center gap-1 text-muted-foreground hover:text-foreground">
              <ChevronDown className="h-3.5 w-3.5 transition-transform" aria-hidden />
              {t('security.totp.wizard.step1.manualEntryDisclosure')}
            </summary>
            <div className="mt-2 space-y-2">
              <code className="block rounded-md border border-border bg-muted/30 px-2 py-1 font-mono text-xs">
                {manualEntryKey}
              </code>
              <Button type="button" variant="outline" size="sm" onClick={copyManualKey}>
                <Copy className="h-3.5 w-3.5" />
                {t('security.totp.wizard.step1.manualEntryCopy')}
              </Button>
            </div>
          </details>
        </div>

        <div className="space-y-3">
          <h2 className="text-base font-medium">{t('security.totp.wizard.step1.heading')}</h2>
          <ol className="list-decimal space-y-1 pl-5 text-sm text-muted-foreground">
            <li>{t('security.totp.wizard.step1.instruction1')}</li>
            <li>{t('security.totp.wizard.step1.instruction2')}</li>
            <li>{t('security.totp.wizard.step1.instruction3')}</li>
          </ol>

          <div className="space-y-2">
            <InputOTP
              maxLength={6}
              value={codeValue}
              onChange={(v) => form.setValue('code', v, { shouldValidate: false })}
              aria-label={t('security.totp.wizard.step1.codeLabel')}
              autoFocus
              disabled={restartRequired || form.formState.isSubmitting}
            >
              <InputOTPGroup aria-invalid={errorMessage != null ? true : undefined}>
                <InputOTPSlot index={0} />
                <InputOTPSlot index={1} />
                <InputOTPSlot index={2} />
                <InputOTPSlot index={3} />
                <InputOTPSlot index={4} />
                <InputOTPSlot index={5} />
              </InputOTPGroup>
            </InputOTP>

            {errorMessage && (
              <div role="alert" aria-live="assertive" className="text-sm text-destructive">
                {errorMessage}
              </div>
            )}
          </div>

          {restartRequired ? (
            <Button type="button" onClick={onRestart}>
              {t('security.totp.wizard.step1.errors.restartButton')}
            </Button>
          ) : (
            <Button
              type="submit"
              disabled={codeValue.length !== 6 || form.formState.isSubmitting}
            >
              {form.formState.isSubmitting
                ? t('security.totp.wizard.step1.submitting')
                : t('security.totp.wizard.step1.submit')}
            </Button>
          )}
        </div>
      </div>
    </form>
  );
}
