import { Link } from 'react-router-dom';
import { toast } from 'sonner';
import { Button, buttonVariants } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useDocumentTitle } from '../../lib/use-document-title';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { useApi } from '../../lib/use-api';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { refetchSettings } from '../../lib/use-settings';
import { SettingsForm } from './SettingsForm';
import {
  CURRENCIES_URL,
  SETTINGS_URL,
  type CurrencyOptionDto,
  type SettingsDto,
  type SettingsFormValues,
  type UpdateSettingsRequest,
} from './settings-api';
import { apiFetch } from '../../lib/api-client';

export function SettingsPage() {
  useDocumentTitle('Settings');
  const settings = useApi<SettingsDto>(SETTINGS_URL);
  const currencies = useApi<CurrencyOptionDto[]>(CURRENCIES_URL);

  const loading = settings.loading || currencies.loading;
  const hasData = !!settings.data && !!currencies.data;
  const hasError = !!(settings.error || currencies.error);
  const showSkeleton = useDelayedLoading(loading && !hasData);

  let state: DataTransitionState;
  if (showSkeleton && !hasData) state = 'skeleton';
  else if (hasError && !hasData) state = 'error';
  else state = 'data';

  function retry() {
    settings.refetch();
    currencies.refetch();
  }

  async function handleSubmit(values: SettingsFormValues) {
    const body: UpdateSettingsRequest = {
      numberFormat:      values.numberFormat,
      dateFormat:        values.dateFormat,
      defaultCurrencyId: values.defaultCurrencyId,
      periodStartDay:    values.periodStartDay,
    };

    try {
      const response = await apiFetch(SETTINGS_URL, {
        method: 'PATCH',
        body,
      });

      if (!response.ok) {
        toast.error("Couldn't save. Try again.");
        return { ok: false as const };
      }

      // Notify every other component using useSettings(). Fire-and-forget;
      // we don't block the success toast on the cache settling.
      void refetchSettings();
      toast.success('Saved.');
      return { ok: true as const };
    } catch {
      toast.error("Couldn't save. Try again.");
      return { ok: false as const };
    }
  }

  const skeleton = (
    <Card data-testid="settings-skeleton">
      <CardHeader>
        <CardTitle>Display preferences</CardTitle>
      </CardHeader>
      <CardContent className="space-y-6">
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
      </CardContent>
    </Card>
  );

  const errorSlot = (
    <Card>
      <CardContent>
        <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          Couldn't load settings.
        </div>
        <Button type="button" variant="outline" className="mt-4" onClick={retry}>
          Retry
        </Button>
      </CardContent>
    </Card>
  );

  return (
    <PageShell>
      <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
        {settings.data && currencies.data && (
          <Card>
            <CardHeader>
              <CardTitle>Display preferences</CardTitle>
            </CardHeader>
            <CardContent>
              <SettingsForm
                initialValues={buildInitialValues(settings.data, currencies.data)}
                currencies={currencies.data}
                onSubmit={handleSubmit}
              />
            </CardContent>
          </Card>
        )}
      </DataTransition>

      <Card>
        <CardHeader>
          <CardTitle>Security</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-muted-foreground text-sm">
            Review the devices signed in to your account and revoke any you don&apos;t recognise.
          </p>
          <Link
            to="/settings/sessions"
            className={cn(buttonVariants({ variant: 'outline' }), 'shrink-0')}
          >
            Active sessions
          </Link>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Account</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-muted-foreground text-sm">
            Manage your personal data, including downloading a copy of everything we hold.
          </p>
          <Link
            to="/settings/account"
            className={cn(buttonVariants({ variant: 'outline' }), 'shrink-0')}
          >
            Manage account
          </Link>
        </CardContent>
      </Card>
    </PageShell>
  );
}

// Convert the SettingsDto (which carries the currency code/symbol for
// display) into form values (which carry the currency *id* for editing).
function buildInitialValues(
  settings: SettingsDto,
  currencies: CurrencyOptionDto[],
): SettingsFormValues {
  return {
    numberFormat:      settings.numberFormat,
    dateFormat:        settings.dateFormat,
    defaultCurrencyId: currencies.find((c) => c.code === settings.defaultCurrencyCode)?.id
                       ?? currencies[0].id,
    periodStartDay:    settings.periodStartDay,
  };
}

/**
 * Page shell matching the design-system grammar (Patterns page convention):
 * `space-y-*` outer container, header with title + muted description, then
 * the page's content sections as Cards.
 */
function PageShell({ children }: { children: React.ReactNode }) {
  return (
    <div className="space-y-8 max-w-2xl">
      <header>
        <h1 className="text-2xl font-semibold">Settings</h1>
        <p className="mt-2 text-muted-foreground">
          Customize how Project Ceres displays numbers, dates, and currency.
        </p>
      </header>
      {children}
    </div>
  );
}
