import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Switch } from '@/components/ui/switch';
import { Separator } from '@/components/ui/separator';
import {
  readConsent,
  writeConsent,
  type ConsentCategories,
} from '../i18n/consent-cookie';
import { onOpenConsentManager } from '../i18n/consent-events';

const DEFAULT_TOGGLES: Pick<ConsentCategories, 'analytics' | 'preferences'> = {
  analytics: false,
  preferences: false,
};

/**
 * Cookie-consent banner, mounted once at the app root. Shows itself when no
 * consent cookie exists yet, and re-opens (in manage mode) whenever
 * `openConsentManager()` fires — e.g. from the footer's "Manage cookies" link.
 */
export function ConsentBanner() {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(() => readConsent() === null);
  const [managing, setManaging] = useState(false);
  const [toggles, setToggles] = useState(DEFAULT_TOGGLES);

  useEffect(() => {
    return onOpenConsentManager(() => {
      const existing = readConsent();
      setToggles({
        analytics: existing?.categories.analytics ?? false,
        preferences: existing?.categories.preferences ?? false,
      });
      setManaging(false);
      setVisible(true);
    });
  }, []);

  if (!visible) return null;

  function acceptAll() {
    writeConsent({ necessary: true, analytics: true, preferences: true });
    setVisible(false);
  }

  function rejectAll() {
    writeConsent({ necessary: true, analytics: false, preferences: false });
    setVisible(false);
  }

  function savePreferences() {
    writeConsent({ necessary: true, ...toggles });
    setVisible(false);
  }

  return (
    <div
      role="region"
      aria-label={t('legal.banner.title')}
      className="fixed inset-x-0 bottom-0 z-50 border-t bg-popover p-4 shadow-lg sm:p-6"
    >
      <div className="mx-auto flex max-w-[65ch] flex-col gap-4">
        <div>
          <p className="font-medium text-foreground">{t('legal.banner.title')}</p>
          <p className="text-sm text-muted-foreground">{t('legal.banner.body')}</p>
        </div>

        {managing && (
          <div className="flex flex-col gap-3">
            <Separator />
            <div className="flex items-center justify-between gap-4">
              <div>
                <p className="text-sm font-medium text-foreground">
                  {t('legal.banner.categories.necessary')}
                </p>
                <p className="text-xs text-muted-foreground">
                  {t('legal.banner.categories.necessaryDesc')}
                </p>
              </div>
              <Switch
                aria-label={t('legal.banner.categories.necessary')}
                checked
                disabled
              />
            </div>
            <div className="flex items-center justify-between gap-4">
              <div>
                <p className="text-sm font-medium text-foreground">
                  {t('legal.banner.categories.analytics')}
                </p>
                <p className="text-xs text-muted-foreground">
                  {t('legal.banner.categories.analyticsDesc')}
                </p>
              </div>
              <Switch
                aria-label={t('legal.banner.categories.analytics')}
                checked={toggles.analytics}
                onCheckedChange={(checked) =>
                  setToggles((prev) => ({ ...prev, analytics: checked }))
                }
              />
            </div>
            <div className="flex items-center justify-between gap-4">
              <div>
                <p className="text-sm font-medium text-foreground">
                  {t('legal.banner.categories.preferences')}
                </p>
                <p className="text-xs text-muted-foreground">
                  {t('legal.banner.categories.preferencesDesc')}
                </p>
              </div>
              <Switch
                aria-label={t('legal.banner.categories.preferences')}
                checked={toggles.preferences}
                onCheckedChange={(checked) =>
                  setToggles((prev) => ({ ...prev, preferences: checked }))
                }
              />
            </div>
          </div>
        )}

        <div className="flex flex-wrap gap-2">
          {managing ? (
            <Button className="min-h-11 min-w-11" onClick={savePreferences}>
              {t('legal.banner.save')}
            </Button>
          ) : (
            <>
              <Button className="min-h-11 min-w-11" onClick={acceptAll}>
                {t('legal.banner.acceptAll')}
              </Button>
              <Button className="min-h-11 min-w-11" onClick={rejectAll}>
                {t('legal.banner.rejectAll')}
              </Button>
              <Button
                variant="outline"
                className="min-h-11 min-w-11"
                onClick={() => setManaging(true)}
              >
                {t('legal.banner.managePreferences')}
              </Button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
