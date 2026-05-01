import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementsBulkActions } from './MovementsBulkActions';
import { toast } from 'sonner';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

function renderAt(search: string, totalCount = 5, onAfterBulk = vi.fn()) {
  return render(
    <MemoryRouter initialEntries={[`/movements${search}`]}>
      <MovementsBulkActions totalCount={totalCount} onAfterBulk={onAfterBulk} />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
});

afterEach(() => {
  vi.resetAllMocks();
});

describe('MovementsBulkActions', () => {
  it('renders both buttons', () => {
    renderAt('');
    expect(screen.getByRole('button', { name: /mark visible cleared/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /export csv/i })).toBeInTheDocument();
  });

  it('disables Mark visible cleared when neither from nor to is in URL params', () => {
    renderAt('');
    expect(screen.getByRole('button', { name: /mark visible cleared/i })).toBeDisabled();
  });

  it('enables Mark visible cleared when at least one date is set; click opens dialog with count', async () => {
    renderAt('?from=2026-01-01', 7);
    const btn = screen.getByRole('button', { name: /mark visible cleared/i });
    expect(btn).not.toBeDisabled();
    fireEvent.click(btn);
    await waitFor(() => {
      expect(screen.getByText(/mark 7 movements as cleared\?/i)).toBeInTheDocument();
    });
  });

  it('confirming the dialog POSTs to /api/movements/bulk-cleared with the correct body including type', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 200, json: async () => ({ cleared: 3 }) });
    const onAfterBulk = vi.fn();
    renderAt('?from=2026-01-01&to=2026-01-31&type=transfer&accountId=acc-1', 3, onAfterBulk);

    fireEvent.click(screen.getByRole('button', { name: /mark visible cleared/i }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /^mark cleared$/i })).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('button', { name: /^mark cleared$/i }));

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        '/api/movements/bulk-cleared',
        expect.objectContaining({ method: 'POST' }),
      );
    });
    const init = mockFetch.mock.calls[0][1];
    const body = JSON.parse((init?.body as string) ?? '{}');
    expect(body).toEqual({
      from: '2026-01-01',
      to: '2026-01-31',
      accountId: 'acc-1',
      type: 'transfer',
      currency: null,
    });
    await waitFor(() => expect(onAfterBulk).toHaveBeenCalled());
  });

  it('includes currency in the bulk POST body when ?currency is in URL params', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 200, json: async () => ({ cleared: 1 }) });
    renderAt('?from=2026-01-01&to=2026-01-31&currency=EUR');

    fireEvent.click(screen.getByRole('button', { name: /mark visible cleared/i }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /^mark cleared$/i })).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('button', { name: /^mark cleared$/i }));

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalled();
    });
    const init = mockFetch.mock.calls[0][1];
    const body = JSON.parse((init?.body as string) ?? '{}');
    expect(body.currency).toBe('EUR');
  });

  it('Export CSV click triggers a hidden anchor download with the current search and shows a toast', () => {
    const clickedAnchors: HTMLAnchorElement[] = [];
    const originalClick = HTMLAnchorElement.prototype.click;
    HTMLAnchorElement.prototype.click = function (this: HTMLAnchorElement) {
      clickedAnchors.push(this);
    };

    try {
      renderAt('?from=2026-01-01&type=transaction');
      fireEvent.click(screen.getByRole('button', { name: /export csv/i }));

      expect(clickedAnchors).toHaveLength(1);
      expect(clickedAnchors[0].getAttribute('href')).toBe(
        '/api/movements/export.csv?from=2026-01-01&type=transaction',
      );
      expect(clickedAnchors[0].hasAttribute('download')).toBe(true);
      expect(toast.success).toHaveBeenCalledWith('Exporting movements…');
    } finally {
      HTMLAnchorElement.prototype.click = originalClick;
    }
  });
});
