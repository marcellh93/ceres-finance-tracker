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

describe('RemindersCard', () => {
  beforeEach(() => {
    vi.spyOn(global, 'fetch');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders muted "No reminders due." when count is 0', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      new Response(JSON.stringify({ netWorth: [], mtd: { currencyCode: 'EUR', currencySymbol: '€', income: 0, expenses: 0, savingsRate: 0 }, remindersDueCount: 0 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    renderCard();
    await screen.findByText('No reminders due.');
  });

  it('renders the count and "View all →" link when count > 0', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      new Response(JSON.stringify({ netWorth: [], mtd: { currencyCode: 'EUR', currencySymbol: '€', income: 0, expenses: 0, savingsRate: 0 }, remindersDueCount: 3 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    renderCard();
    expect(await screen.findByText(/3/)).toBeDefined();
    expect(await screen.findByText(/reminders? due/)).toBeDefined();
    expect(screen.getByRole('link', { name: /view all/i })).toBeDefined();
  });

  it('renders the error state with a Retry button on fetch failure', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockRejectedValue(new Error('boom'));
    renderCard();
    expect(await screen.findByRole('button', { name: 'Retry' })).toBeDefined();
    expect(await screen.findByText(/couldn't load/i)).toBeDefined();
  });

  it('clicking Retry re-runs the request', async () => {
    const fetchSpy = global.fetch as ReturnType<typeof vi.spyOn>;
    fetchSpy
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ netWorth: [], mtd: { currencyCode: 'EUR', currencySymbol: '€', income: 0, expenses: 0, savingsRate: 0 }, remindersDueCount: 1 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    const user = userEvent.setup();
    renderCard();
    await user.click(await screen.findByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull());
    expect(await screen.findByText(/1/)).toBeDefined();
  });
});
