import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { ThemeProvider } from '@/app/theme/theme-context';
import { AuthProvider } from '../../auth/auth-context';
import { AuthLayout } from '../../layout/AuthLayout';
import { LoginTotp } from './LoginTotp';
import { expectNoA11yViolations } from '../../lib/test-axe';

describe('LoginTotp a11y', () => {
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

  function mount() {
    return render(
      <ThemeProvider>
        <I18nextProvider i18n={i18n}>
          <AuthProvider>
            <MemoryRouter initialEntries={['/login/totp']}>
              <Routes>
                <Route element={<AuthLayout />}>
                  <Route path="login/totp" element={<LoginTotp />} />
                </Route>
              </Routes>
            </MemoryRouter>
          </AuthProvider>
        </I18nextProvider>
      </ThemeProvider>,
    );
  }

  it(
    'TOTP state has no serious/critical axe violations',
    async () => {
      const { container } = mount();
      await expectNoA11yViolations(container);
    },
    20_000,
  );

  it(
    'backup-code state has no serious/critical axe violations',
    async () => {
      const { container, getByRole } = mount();
      const user = userEvent.setup();
      await user.click(getByRole('button', { name: /lost your device/i }));
      await expectNoA11yViolations(container);
    },
    20_000,
  );
});
