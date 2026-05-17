import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TopBar } from './TopBar';
import { ReminderCountProvider } from './ReminderCountProvider';
import { ThemeProvider } from '@/app/theme/theme-context';
import { AuthProvider } from '@/app/auth/auth-context';

beforeEach(() => {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: true, // pretend we're on desktop
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
  // ReminderCountProvider fetches reminders, AuthProvider fetches /api/auth/me
  // via apiFetch (which calls response.headers.get(...)), so the mock must
  // return real Response objects, not bare {ok, json} stubs.
  global.fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/api/auth/me')) {
      // 401 anon — TopBar tests don't care about auth state
      return new Response(
        JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      );
    }
    // Default: empty array (ReminderCountProvider / ReviewCountProvider)
    return new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } });
  }) as unknown as typeof fetch;
});

function renderTopBar(route: string) {
  return render(
    <ThemeProvider>
      <AuthProvider>
        <MemoryRouter initialEntries={[route]}>
          <ReminderCountProvider>
            <TopBar onMenuClick={vi.fn()} />
          </ReminderCountProvider>
        </MemoryRouter>
      </AuthProvider>
    </ThemeProvider>,
  );
}

describe('TopBar quick-add suppression', () => {
  it('renders the Quick add button on the dashboard route', () => {
    renderTopBar('/');
    expect(screen.getByRole('button', { name: /quick add/i })).toBeInTheDocument();
  });

  it('hides the Quick add button on /movements', () => {
    renderTopBar('/movements');
    expect(screen.queryByRole('button', { name: /quick add/i })).toBeNull();
  });

  it('hides the Quick add button on /movements/new', () => {
    renderTopBar('/movements/new');
    expect(screen.queryByRole('button', { name: /quick add/i })).toBeNull();
  });

  it('hides the Quick add button on /movements/abc/edit', () => {
    renderTopBar('/movements/abc/edit');
    expect(screen.queryByRole('button', { name: /quick add/i })).toBeNull();
  });
});

describe('TopBar — bell badge', () => {
  function renderWithCount(count: number) {
    const items = Array.from({ length: count }, (_, i) => ({
      id: String(i), name: `R${i}`, estimatedAmount: null, accountId: 'a', accountName: 'A',
      currencySymbol: '€', categoryId: 'c', categoryName: 'C', categoryTypeName: 'Expense',
      frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-03',
      isActive: true, reminderBehaviour: 'SnapToCalendarDay',
    }));
    global.fetch = vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/me')) {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(JSON.stringify(items), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }) as unknown as typeof fetch;
    renderTopBar('/');
  }

  it('shows no badge when count=0', async () => {
    renderWithCount(0);
    await waitFor(() =>
      expect(screen.queryByText(/^\d+$|^9\+$/)).not.toBeInTheDocument()
    );
  });

  it('shows badge count when count > 0', async () => {
    renderWithCount(3);
    await waitFor(() => expect(screen.getByText('3')).toBeInTheDocument());
  });

  it('shows 9+ when count > 9', async () => {
    renderWithCount(12);
    await waitFor(() => expect(screen.getByText('9+')).toBeInTheDocument());
  });
});
