import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { RemindersCard } from './RemindersCard';

function renderCard() {
  return render(
    <MemoryRouter>
      <RemindersCard />
    </MemoryRouter>,
  );
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('RemindersCard', () => {
  beforeEach(() => {
    vi.spyOn(global, 'fetch');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders the "All caught up" empty state when no upcoming reminders', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      jsonResponse({ totalCount: 0, items: [] }),
    );
    renderCard();
    await screen.findByText('All caught up');
  });

  it('renders the list of upcoming reminders with name and account', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      jsonResponse({
        totalCount: 2,
        items: [
          { id: 'r1', name: 'Rent', nextDueDate: '2026-05-05', estimatedAmount: 1200, currencySymbol: '€', accountName: 'Checking' },
          { id: 'r2', name: 'Netflix', nextDueDate: '2026-05-07', estimatedAmount: 12.99, currencySymbol: '€', accountName: 'Credit Card' },
        ],
      }),
    );
    renderCard();
    expect(await screen.findByText('Rent')).toBeInTheDocument();
    expect(screen.getByText('Netflix')).toBeInTheDocument();
    expect(screen.getByText(/Checking/)).toBeInTheDocument();
    expect(screen.getByText(/Credit Card/)).toBeInTheDocument();
  });

  it('renders +N more overflow row when totalCount exceeds items length', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      jsonResponse({
        totalCount: 12,
        items: [
          { id: 'r1', name: 'Rent', nextDueDate: '2026-05-05', estimatedAmount: 1200, currencySymbol: '€', accountName: 'Checking' },
          { id: 'r2', name: 'Netflix', nextDueDate: '2026-05-07', estimatedAmount: 12.99, currencySymbol: '€', accountName: 'Card' },
          { id: 'r3', name: 'Gym', nextDueDate: '2026-05-08', estimatedAmount: 30, currencySymbol: '€', accountName: 'Card' },
          { id: 'r4', name: 'Spotify', nextDueDate: '2026-05-09', estimatedAmount: 9.99, currencySymbol: '€', accountName: 'Card' },
          { id: 'r5', name: 'Internet', nextDueDate: '2026-05-10', estimatedAmount: 50, currencySymbol: '€', accountName: 'Checking' },
        ],
      }),
    );
    renderCard();
    expect(await screen.findByText('+7 more')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /view all/i })).toBeInTheDocument();
  });

  it('renders the error state with a Retry button on fetch failure', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockRejectedValue(new Error('boom'));
    renderCard();
    expect(await screen.findByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(await screen.findByText(/couldn't load/i)).toBeInTheDocument();
  });

  it('clicking Retry re-runs the request', async () => {
    const fetchSpy = global.fetch as ReturnType<typeof vi.spyOn>;
    fetchSpy
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce(
        jsonResponse({
          totalCount: 1,
          items: [{ id: 'r1', name: 'Rent', nextDueDate: '2026-05-05', estimatedAmount: 1200, currencySymbol: '€', accountName: 'Checking' }],
        }),
      );
    const user = userEvent.setup();
    renderCard();
    await user.click(await screen.findByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull());
    expect(await screen.findByText('Rent')).toBeInTheDocument();
  });
});
