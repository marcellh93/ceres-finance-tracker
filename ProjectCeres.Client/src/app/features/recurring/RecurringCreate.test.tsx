import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RecurringCreate } from './RecurringCreate';
import { primeCsrfToken } from '../../../test/csrf-fetch-mock';

const CTX = { refetch: vi.fn(), refreshBell: vi.fn() };

function renderCreate() {
  return render(
    <MemoryRouter initialEntries={['/recurring/new']}>
      <Routes>
        <Route path="/recurring/new" element={<RecurringCreate ctx={CTX} />} />
        <Route path="/recurring" element={<div>List page</div>} />
      </Routes>
    </MemoryRouter>
  );
}

describe('RecurringCreate', () => {
  beforeEach(async () => {
  await primeCsrfToken();
    CTX.refetch.mockClear();
    CTX.refreshBell.mockClear();
    global.fetch = vi.fn().mockResolvedValue({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => [] });
  });

  it('renders the form card with title "New reminder"', async () => {
    renderCreate();
    await waitFor(() => expect(screen.getByText('New reminder')).toBeInTheDocument());
  });

  it('calls ctx.refetch and ctx.refreshBell after successful POST', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => [] });

    const { container } = renderCreate();
    // Wait for loading state to resolve and form to render
    await waitFor(() => expect(screen.getByText('New reminder')).toBeInTheDocument());

    // Submit the form directly — bypasses field validation to test ctx calls
    const form = container.querySelector('form')!;
    fireEvent.submit(form);

    await waitFor(() => {
      expect(CTX.refetch).toHaveBeenCalled();
      expect(CTX.refreshBell).toHaveBeenCalled();
    });
  });
});
