import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { LanguageToggle } from './LanguageToggle';

function renderToggle() {
  return render(
    <I18nextProvider i18n={i18n}>
      <LanguageToggle />
    </I18nextProvider>,
  );
}

describe('LanguageToggle', () => {
  beforeEach(async () => {
    document.cookie = 'lang=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
    await act(() => i18n.changeLanguage('en'));
  });

  afterEach(async () => {
    document.cookie = 'lang=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
    // Reset language inside act() so any pending react-i18next subscriber
    // state updates are flushed before the next beforeEach runs.
    await act(() => i18n.changeLanguage('en'));
  });

  it('renders the globe trigger with an aria-label that includes the active language', () => {
    renderToggle();
    expect(screen.getByRole('button', { name: /change language/i })).toBeDefined();
  });

  it('switches the language and writes the lang cookie when an option is selected', async () => {
    const user = userEvent.setup();
    renderToggle();
    await user.click(screen.getByRole('button', { name: /change language/i }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Español' }));

    expect(i18n.language).toBe('es');
    expect(document.cookie).toContain('lang=es');

    // Wait for the trigger's aria-label to reflect the new language — this
    // confirms the i18n.changeLanguage('es') Promise inside choose() has fully
    // resolved and all languageChanged subscribers have fired. Without this,
    // the choose() Promise can still be in flight when the next test's
    // beforeEach runs, racing the cleanup and leaving i18n.language='es'.
    await screen.findByRole('button', { name: /cambiar idioma/i });
  });

  it('renders the active language code (EN) on the trigger in English', () => {
    renderToggle();
    const trigger = screen.getByRole('button', { name: /change language/i });
    expect(within(trigger).getByText('EN')).toBeDefined();
  });

  it('aria-label interpolates the active language name and updates on language change', async () => {
    renderToggle();
    // Initial render — English. Aria-label includes "currently English".
    expect(
      screen.getByRole('button', { name: /change language, currently english/i }),
    ).toBeDefined();

    // Switch to Spanish — wrap in act() so react-i18next subscriber updates flush.
    await act(() => i18n.changeLanguage('es'));

    // After re-render, aria-label is in Spanish and includes "actualmente Español".
    expect(
      screen.getByRole('button', { name: /cambiar idioma, actualmente español/i }),
    ).toBeDefined();
  });

  it('marks the currently-active language as checked in the dropdown', async () => {
    const user = userEvent.setup();
    renderToggle();

    // Phase 1: open menu while English is active — verify English is checked.
    await user.click(screen.getByRole('button', { name: /change language/i }));
    const englishItem = await screen.findByRole('menuitemradio', { name: 'English' });
    const spanishItem = screen.getByRole('menuitemradio', { name: 'Español' });
    expect(englishItem.getAttribute('aria-checked')).toBe('true');
    expect(spanishItem.getAttribute('aria-checked')).toBe('false');

    // Close menu cleanly via Escape before changing language.
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('menuitemradio')).toBeNull());

    // Phase 2: change language to Spanish directly via act() — this avoids the
    // multi-step user.click-item → menu-auto-close → re-open sequence that is
    // unreliable under parallel-suite CPU load (base-ui's popup open/close
    // uses requestAnimationFrame which jsdom defers under heavy concurrency).
    await act(() => i18n.changeLanguage('es'));

    // Open a fresh menu and verify Español is now checked.
    await user.click(screen.getByRole('button', { name: /cambiar idioma/i }));
    await waitFor(() => {
      const eng = screen.getByRole('menuitemradio', { name: 'English' });
      const esp = screen.getByRole('menuitemradio', { name: 'Español' });
      expect(eng.getAttribute('aria-checked')).toBe('false');
      expect(esp.getAttribute('aria-checked')).toBe('true');
    });
  });
});
