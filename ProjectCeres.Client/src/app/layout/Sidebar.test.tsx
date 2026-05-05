import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { SIDEBAR_STORAGE_KEY } from '../lib/sidebar-storage';
import { ReviewCountProvider } from '../features/review/ReviewCountProvider';

function renderSidebar(initialPath: string = '/movements') {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Sidebar />
    </MemoryRouter>,
  );
}

describe('Sidebar', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('renders all nav items in three groups + two bottom-pinned items', () => {
    renderSidebar();
    const expectedLabels = [
      // Activity
      'Movements', 'Review',
      // Money
      'Accounts', 'Categories', 'Budgets',
      // Tools
      'Recurring Transactions', 'Import', 'Reports',
      // Bottom-pinned
      'Settings', 'Support',
    ];
    for (const label of expectedLabels) {
      expect(screen.getByRole('link', { name: label })).toBeDefined();
    }
  });

  it('renders the three group headings in order', () => {
    renderSidebar();
    const headings = screen.getAllByRole('heading', { level: 2 });
    expect(headings.map((h) => h.textContent?.trim())).toEqual([
      'Activity',
      'Money',
      'Tools',
    ]);
  });

  it('marks the active route with aria-current="page"', () => {
    renderSidebar('/movements');
    const active = screen.getByRole('link', { name: 'Movements' });
    expect(active.getAttribute('aria-current')).toBe('page');
    const inactive = screen.getByRole('link', { name: 'Review' });
    expect(inactive.getAttribute('aria-current')).toBeNull();
  });

  it('collapse toggle has aria-expanded reflecting the current state', async () => {
    const user = userEvent.setup();
    renderSidebar();
    const toggle = screen.getByRole('button', { name: /collapse sidebar/i });
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    await user.click(toggle);
    const expanded = await screen.findByRole('button', { name: /expand sidebar/i });
    expect(expanded.getAttribute('aria-expanded')).toBe('false');
  });

  it('persists the collapse choice to localStorage', async () => {
    const user = userEvent.setup();
    renderSidebar();
    await user.click(screen.getByRole('button', { name: /collapse sidebar/i }));
    expect(localStorage.getItem(SIDEBAR_STORAGE_KEY)).toBe('true');
  });
});

function mockReviewCounts(reconciliation: number, transfer: number) {
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) => {
      if (url.endsWith('/reconciliation-review/pending/count')) {
        return Promise.resolve({ ok: true, json: async () => reconciliation } as Response);
      }
      if (url.endsWith('/transfer-review/pending/count')) {
        return Promise.resolve({ ok: true, json: async () => transfer } as Response);
      }
      return Promise.reject(new Error(`Unexpected URL: ${url}`));
    }) as typeof fetch,
  );
}

describe('Sidebar — Review badge', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('renders Review badge when total > 0', async () => {
    mockReviewCounts(2, 1);
    render(
      <MemoryRouter>
        <ReviewCountProvider>
          <Sidebar />
        </ReviewCountProvider>
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByTestId('nav-badge-review').textContent).toBe('3'));
  });

  it('hides Review badge when total === 0', async () => {
    mockReviewCounts(0, 0);
    render(
      <MemoryRouter>
        <ReviewCountProvider>
          <Sidebar />
        </ReviewCountProvider>
      </MemoryRouter>,
    );
    // Wait for fetches to settle, then assert absence.
    await waitFor(() =>
      expect((globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(0),
    );
    expect(screen.queryByTestId('nav-badge-review')).toBeNull();
  });

  it('aria-label on Review link includes the count when present', async () => {
    mockReviewCounts(2, 1);
    render(
      <MemoryRouter>
        <ReviewCountProvider>
          <Sidebar />
        </ReviewCountProvider>
      </MemoryRouter>,
    );
    await waitFor(() =>
      expect(screen.getByRole('link', { name: /Review, 3 pending/ })).toBeInTheDocument(),
    );
  });
});
