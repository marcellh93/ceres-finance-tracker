import { render, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '@/app/i18n/i18n';
import { ThemeProvider } from '@/app/theme/theme-context';
import { AuthLayout } from './AuthLayout';

describe('AuthLayout', () => {
  it('uses bg-background for the page surface so the card has light-mode separation', () => {
    // Regression test for Stage 9.1.5.c. Previously used bg-muted/30, which
    // composited to ~oklch(0.989) in light mode against a card of oklch(1.000)
    // — card edge invisible. bg-background (1.000) lets the card's border + shadow
    // do the separation, which is the canonical shadcn pattern.
    const { container } = render(
      <ThemeProvider>
        <I18nextProvider i18n={i18n}>
          <MemoryRouter initialEntries={['/']}>
            <Routes>
              <Route element={<AuthLayout />}>
                <Route index element={<div>test</div>} />
              </Route>
            </Routes>
          </MemoryRouter>
        </I18nextProvider>
      </ThemeProvider>,
    );
    const pageDiv = container.querySelector('.min-h-dvh');
    expect(pageDiv).not.toBeNull();
    expect(pageDiv).toHaveClass('bg-background');
    expect(pageDiv).not.toHaveClass('bg-muted/30');
    expect(pageDiv).not.toHaveClass('bg-muted');
  });

  it('mounts the shared Footer (legal links + manage-cookies + language toggle) and ThemeToggle', () => {
    const { container } = render(
      <ThemeProvider>
        <I18nextProvider i18n={i18n}>
          <MemoryRouter initialEntries={['/']}>
            <Routes>
              <Route element={<AuthLayout />}>
                <Route index element={<div>test</div>} />
              </Route>
            </Routes>
          </MemoryRouter>
        </I18nextProvider>
      </ThemeProvider>,
    );
    const footer = container.querySelector('[data-slot="auth-footer"]');
    expect(footer).not.toBeNull();
    const scope = within(footer as HTMLElement);
    // Stage 13: AuthLayout now mounts the shared <Footer/> (legal links +
    // manage-cookies button + language-toggle trigger) alongside ThemeToggle,
    // replacing the old bare 2-button footer. 3 buttons total: manage-cookies,
    // LanguageToggle trigger, ThemeToggle trigger.
    const buttons = scope.getAllByRole('button');
    expect(buttons).toHaveLength(3);
    expect(scope.getByRole('button', { name: i18n.t('legal.footer.manageCookies') })).toBeInTheDocument();
    expect(scope.getByRole('link', { name: i18n.t('legal.footer.privacy') })).toBeInTheDocument();
    expect(scope.getByRole('link', { name: i18n.t('legal.footer.avisoLegal') })).toBeInTheDocument();
    expect(scope.getByRole('link', { name: i18n.t('legal.footer.cookiePolicy') })).toBeInTheDocument();
  });
});
