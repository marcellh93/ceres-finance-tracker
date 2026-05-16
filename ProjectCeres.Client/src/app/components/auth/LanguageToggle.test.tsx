import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { LanguageToggle } from './LanguageToggle';

describe('LanguageToggle', () => {
  beforeEach(() => {
    document.cookie = 'lang=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
    void i18n.changeLanguage('en');
  });

  afterEach(() => {
    document.cookie = 'lang=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  });

  it('renders the globe button with an accessible label', () => {
    render(
      <I18nextProvider i18n={i18n}>
        <LanguageToggle />
      </I18nextProvider>,
    );
    expect(screen.getByRole('button', { name: /change language/i })).toBeDefined();
  });

  it('switches the language and writes the lang cookie when an option is selected', async () => {
    const user = userEvent.setup();
    render(
      <I18nextProvider i18n={i18n}>
        <LanguageToggle />
      </I18nextProvider>,
    );
    await user.click(screen.getByRole('button', { name: /change language/i }));
    await user.click(await screen.findByRole('menuitem', { name: 'Español' }));

    expect(i18n.language).toBe('es');
    expect(document.cookie).toContain('lang=es');
  });
});
