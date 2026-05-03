import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RecurringLayout } from './RecurringLayout';

function renderLayout(path = '/recurring') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="recurring" element={<RecurringLayout />}>
          <Route path="new" element={<div>Create form</div>} />
          <Route path=":id/edit" element={<div>Edit form</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  );
}

describe('RecurringLayout', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('shows skeleton while loading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation(() => new Promise(() => {}));
    renderLayout();
    expect(screen.getByTestId('recurring-skeleton')).toBeInTheDocument();
  });

  it('renders h1 "Recurring transactions"', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderLayout();
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Recurring transactions' })).toBeInTheDocument()
    );
  });

  it('renders first-run empty state when no reminders exist', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderLayout();
    await waitFor(() => expect(screen.getByText(/No recurring reminders yet/i)).toBeInTheDocument());
  });

  it('renders child route when on /recurring/new (hides list chrome)', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderLayout('/recurring/new');
    await waitFor(() => expect(screen.getByText('Create form')).toBeInTheDocument());
    expect(screen.queryByRole('heading', { name: 'Recurring transactions' })).not.toBeInTheDocument();
  });
});
