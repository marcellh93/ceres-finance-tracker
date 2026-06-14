import { render } from '@testing-library/react';
import { afterEach, beforeEach, describe, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { ThemeProvider } from '@/app/theme/theme-context';
import { AuthProvider } from '../../auth/auth-context';
import { AuthLayout } from '../../layout/AuthLayout';
import { PasswordReset } from './PasswordReset';
import { expectNoA11yViolations } from '../../lib/test-axe';

describe('PasswordReset a11y', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    fetchSpy.mockResolvedValue(
      new Response(
        JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      ),
    );
  });

  afterEach(() => vi.restoreAllMocks());

  function mount(initialPath: string) {
    return render(
      <ThemeProvider>
        <I18nextProvider i18n={i18n}>
          <AuthProvider>
            <MemoryRouter initialEntries={[initialPath]}>
              <Routes>
                <Route element={<AuthLayout />}>
                  <Route path="password-reset" element={<PasswordReset />} />
                </Route>
              </Routes>
            </MemoryRouter>
          </AuthProvider>
        </I18nextProvider>
      </ThemeProvider>,
    );
  }

  it(
    'request form has no serious/critical axe violations',
    async () => {
      const { container } = mount('/password-reset');
      await expectNoA11yViolations(container);
    },
    20_000,
  );

  it(
    'confirm form has no serious/critical axe violations',
    async () => {
      // readTokenFromHash parses #token=<value> via URLSearchParams.
      // Any non-empty value causes PasswordReset to render <ConfirmForm>.
      const { container } = mount('/password-reset#token=abc');
      await expectNoA11yViolations(container);
    },
    20_000,
  );
});
