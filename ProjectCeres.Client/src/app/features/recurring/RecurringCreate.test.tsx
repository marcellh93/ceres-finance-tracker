import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RecurringCreate } from './RecurringCreate';

const CTX = { refetch: vi.fn(), refreshBell: vi.fn() };

function renderCreate() {
  return render(
    <MemoryRouter initialEntries={['/recurring/new']}>
      <Routes>
        <Route path="recurring/new" element={<RecurringCreate ctx={CTX} />} />
        <Route path="recurring" element={<div>List page</div>} />
      </Routes>
    </MemoryRouter>
  );
}

describe('RecurringCreate', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [] });
  });

  it('renders the form card with title "New reminder"', async () => {
    renderCreate();
    await waitFor(() => expect(screen.getByText('New reminder')).toBeInTheDocument());
  });
});
