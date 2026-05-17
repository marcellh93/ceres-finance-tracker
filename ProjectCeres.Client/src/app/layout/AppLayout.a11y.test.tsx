import { render } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { beforeEach, describe, it, vi } from 'vitest';
import { AppLayout } from './AppLayout';
import { AuthProvider } from '@/app/auth/auth-context';
import { ThemeProvider } from '@/app/theme/theme-context';
import { expectNoA11yViolations } from '../lib/test-axe';

// AppLayout renders <Toaster /> from @/components/ui/sonner, which in turn
// wraps the real `sonner` Toaster and calls `useTheme` from @/app/theme/theme-context.
// Mock the local wrapper so JSDOM doesn't choke on portals / canvas.
vi.mock('@/components/ui/sonner', () => ({
  Toaster: () => null,
}));

beforeEach(() => {
  // AuthProvider, ReminderCountProvider, and ReviewCountProvider all fetch on
  // mount. AuthProvider uses apiFetch which calls response.headers.get(...),
  // so the mock must return real Response objects (not bare {ok, json} stubs).
  global.fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/api/auth/me')) {
      return new Response(
        JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      );
    }
    if (url.includes('/pending/count')) {
      return new Response('0', { status: 200, headers: { 'Content-Type': 'application/json' } });
    }
    return new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } });
  }) as unknown as typeof fetch;
});

describe('AppLayout a11y', () => {
  it('renders without serious or critical axe violations', async () => {
    const { container } = render(
      <ThemeProvider>
        <AuthProvider>
          <MemoryRouter initialEntries={['/']}>
            <Routes>
              <Route element={<AppLayout />}>
                <Route index element={<main><h1>Test page</h1></main>} />
              </Route>
            </Routes>
          </MemoryRouter>
        </AuthProvider>
      </ThemeProvider>,
    );
    await expectNoA11yViolations(container);
  });
});
