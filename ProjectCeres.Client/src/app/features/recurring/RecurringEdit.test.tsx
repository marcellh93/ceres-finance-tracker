import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RecurringEdit } from './RecurringEdit';

const CTX = { refetch: vi.fn(), refreshBell: vi.fn() };

describe('RecurringEdit', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('renders form pre-populated with reminder name', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (url.includes('/api/recurring-transactions/r1')) {
        return Promise.resolve({ ok: true, status: 200, json: async () => ({
          id: 'r1', name: 'Rent', estimatedAmount: 900, accountId: 'a1', categoryId: 'c1',
          frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-15',
          isActive: true, reminderBehaviour: 'SnapToCalendarDay',
        }) });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
    render(
      <MemoryRouter initialEntries={['/recurring/r1/edit']}>
        <Routes>
          <Route path="recurring/:id/edit" element={<RecurringEdit ctx={CTX} />} />
          <Route path="recurring" element={<div>List</div>} />
        </Routes>
      </MemoryRouter>
    );
    await waitFor(() =>
      expect((screen.getByLabelText(/name \*/i) as HTMLInputElement).value).toBe('Rent')
    );
  });

  it('shows not-found banner on 404', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: false, status: 404 });
    render(
      <MemoryRouter initialEntries={['/recurring/unknown/edit']}>
        <Routes>
          <Route path="recurring/:id/edit" element={<RecurringEdit ctx={CTX} />} />
        </Routes>
      </MemoryRouter>
    );
    await waitFor(() => expect(screen.getByText(/doesn't exist/i)).toBeInTheDocument());
  });
});
