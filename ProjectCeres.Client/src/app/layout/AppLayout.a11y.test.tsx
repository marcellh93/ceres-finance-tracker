import { render } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { beforeEach, describe, it, vi } from 'vitest';
import { AppLayout } from './AppLayout';
import { expectNoA11yViolations } from '../lib/test-axe';

// AppLayout renders <Toaster /> from @/components/ui/sonner, which in turn
// wraps the real `sonner` Toaster and calls `useTheme` from @/app/theme/theme-context.
// Mock the local wrapper so JSDOM doesn't choke on portals / canvas.
vi.mock('@/components/ui/sonner', () => ({
  Toaster: () => null,
}));

beforeEach(() => {
  // ReminderCountProvider and ReviewCountProvider both fetch on mount.
  // Returning empty/zero responses keeps them happy without surfacing badges.
  global.fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/pending/count')) {
      return { ok: true, json: async () => 0 } as Response;
    }
    return { ok: true, json: async () => [] } as Response;
  }) as unknown as typeof fetch;
});

describe('AppLayout a11y', () => {
  it('renders without serious or critical axe violations', async () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<main><h1>Test page</h1></main>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    await expectNoA11yViolations(container);
  });
});
