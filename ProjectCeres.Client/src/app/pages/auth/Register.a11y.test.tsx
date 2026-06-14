import { render } from '@testing-library/react';
import { afterEach, beforeEach, describe, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { ThemeProvider } from '@/app/theme/theme-context';
import { AuthProvider } from '../../auth/auth-context';
import { AuthLayout } from '../../layout/AuthLayout';
import { Register } from './Register';
import { expectNoA11yViolations } from '../../lib/test-axe';

describe('Register a11y', () => {
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

  it(
    'renders without serious or critical axe violations',
    async () => {
      const { container } = render(
        <ThemeProvider>
          <I18nextProvider i18n={i18n}>
            <AuthProvider>
              <MemoryRouter initialEntries={['/register']}>
                <Routes>
                  <Route element={<AuthLayout />}>
                    <Route path="register" element={<Register />} />
                  </Route>
                </Routes>
              </MemoryRouter>
            </AuthProvider>
          </I18nextProvider>
        </ThemeProvider>,
      );
      await expectNoA11yViolations(container);
    },
    20_000,
  );
});
