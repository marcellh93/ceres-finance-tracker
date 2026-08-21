import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RecurringEdit } from './RecurringEdit';
import { primeCsrfToken } from '../../../test/csrf-fetch-mock';

const CTX = { refetch: vi.fn(), refreshBell: vi.fn() };

describe('RecurringEdit', () => {
  beforeEach(async () => {
  await primeCsrfToken();
    CTX.refetch.mockClear();
    CTX.refreshBell.mockClear();
    global.fetch = vi.fn();
  });

  it('renders form pre-populated with reminder name', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (url.endsWith('/api/recurring-transactions/r1')) {
        return Promise.resolve({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, status: 200, json: async () => ({
          id: 'r1', name: 'Rent', estimatedAmount: 900, accountId: 'a1', categoryId: 'c1',
          frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-15',
          isActive: true, reminderBehaviour: 'SnapToCalendarDay',
        }) });
      }
      return Promise.resolve({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => [] });
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
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: false, status: 404, json: async () => ({}) });
    render(
      <MemoryRouter initialEntries={['/recurring/unknown/edit']}>
        <Routes>
          <Route path="recurring/:id/edit" element={<RecurringEdit ctx={CTX} />} />
        </Routes>
      </MemoryRouter>
    );
    await waitFor(() => expect(screen.getByText(/doesn't exist/i)).toBeInTheDocument());
  });

  it('calls ctx.refetch and ctx.refreshBell after successful PATCH', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string, init?: RequestInit) => {
      if (url.endsWith('/api/recurring-transactions/r1') && init?.method === 'PATCH') {
        return Promise.resolve({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => ({}) });
      }
      if (url.endsWith('/api/recurring-transactions/r1')) {
        return Promise.resolve({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, status: 200, json: async () => ({
          id: 'r1', name: 'Rent', estimatedAmount: 900, accountId: 'a1', categoryId: 'c1',
          frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-15',
          isActive: true, reminderBehaviour: 'SnapToCalendarDay',
        }) });
      }
      return Promise.resolve({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => [] });
    });

    const { container } = render(
      <MemoryRouter initialEntries={['/recurring/r1/edit']}>
        <Routes>
          <Route path="recurring/:id/edit" element={<RecurringEdit ctx={CTX} />} />
          <Route path="recurring" element={<div>List</div>} />
        </Routes>
      </MemoryRouter>
    );

    // Wait for form to load with populated data
    await waitFor(() =>
      expect((screen.getByLabelText(/name \*/i) as HTMLInputElement).value).toBe('Rent')
    );

    // Submit the form directly
    const form = container.querySelector('form')!;
    fireEvent.submit(form);

    await waitFor(() => {
      expect(CTX.refetch).toHaveBeenCalled();
      expect(CTX.refreshBell).toHaveBeenCalled();
    });
  });
});
