import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { I18nextProvider } from 'react-i18next';
import i18n from '@/app/i18n/i18n';
import { ConsentBanner } from './ConsentBanner';
import { CONSENT_COOKIE_NAME, readConsent } from '../i18n/consent-cookie';
import { openConsentManager } from '../i18n/consent-events';

function renderBanner() {
  return render(
    <I18nextProvider i18n={i18n}>
      <ConsentBanner />
    </I18nextProvider>,
  );
}

beforeEach(() => {
  document.cookie = `${CONSENT_COOKIE_NAME}=; Path=/; Max-Age=0`;
});

describe('ConsentBanner', () => {
  it('shows when no consent cookie is present', () => {
    renderBanner();
    expect(screen.getByRole('button', { name: /accept all/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /reject all/i })).toBeInTheDocument();
  });

  it('accept-all and reject-all use the same button variant (equal prominence)', () => {
    renderBanner();
    const accept = screen.getByRole('button', { name: /accept all/i });
    const reject = screen.getByRole('button', { name: /reject all/i });
    expect(accept.className).toEqual(reject.className);
  });

  it('accept-all persists consent and hides the banner', async () => {
    renderBanner();
    await userEvent.click(screen.getByRole('button', { name: /accept all/i }));
    expect(readConsent()!.categories.analytics).toBe(true);
    expect(screen.queryByRole('button', { name: /accept all/i })).not.toBeInTheDocument();
  });

  it('reject-all persists necessary-only and hides the banner', async () => {
    renderBanner();
    await userEvent.click(screen.getByRole('button', { name: /reject all/i }));
    const rec = readConsent()!;
    expect(rec.categories.necessary).toBe(true);
    expect(rec.categories.analytics).toBe(false);
    expect(rec.categories.preferences).toBe(false);
  });

  it('does not show when a consent cookie already exists', async () => {
    renderBanner();
    // dismiss first
    await userEvent.click(screen.getByRole('button', { name: /accept all/i }));
    const { container } = renderBanner();
    expect(container.querySelector('button')).toBeNull();
  });

  it('re-opens on openConsentManager even after a choice', async () => {
    renderBanner();
    await userEvent.click(screen.getByRole('button', { name: /accept all/i }));
    expect(screen.queryByRole('button', { name: /accept all/i })).not.toBeInTheDocument();
    openConsentManager();
    expect(await screen.findByRole('button', { name: /accept all/i })).toBeInTheDocument();
  });

  it('manage preferences reveals the analytics + preferences toggles', async () => {
    renderBanner();
    await userEvent.click(screen.getByRole('button', { name: /manage preferences/i }));
    expect(screen.getByRole('switch', { name: /analytics/i })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: /preferences/i })).toBeInTheDocument();
  });
});
