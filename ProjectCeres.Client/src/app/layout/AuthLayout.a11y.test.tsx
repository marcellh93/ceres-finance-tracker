import { render } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, it } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import i18n from '@/app/i18n/i18n';
import { ThemeProvider } from '@/app/theme/theme-context';
import { AuthLayout } from './AuthLayout';
import { expectNoA11yViolations } from '../lib/test-axe';

describe('AuthLayout a11y', () => {
  it('renders without serious or critical axe violations', async () => {
    const { container } = render(
      <ThemeProvider>
        <I18nextProvider i18n={i18n}>
          <MemoryRouter initialEntries={['/login']}>
            <Routes>
              <Route element={<AuthLayout />}>
                <Route path="login" element={<main><h1>Test page</h1></main>} />
              </Route>
            </Routes>
          </MemoryRouter>
        </I18nextProvider>
      </ThemeProvider>,
    );
    await expectNoA11yViolations(container);
  });
});
