import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminTicketListPage } from './AdminTicketListPage';
import {
  SupportTicketPriority,
  SupportTicketStatus,
} from '../support/support-api';
import type { AdminTicketListItemDto, AdminTicketListResponse } from './admin-support-api';

const navigate = vi.fn();
vi.mock('react-router-dom', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  useNavigate: () => navigate,
}));

const apiFetch = vi.fn();
vi.mock('../../lib/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../lib/api-client')>()),
  apiFetch: (...args: unknown[]) => apiFetch(...args),
}));

function item(over: Partial<AdminTicketListItemDto> = {}): AdminTicketListItemDto {
  return {
    id: '11111111-0000-0000-0000-000000000001',
    subject: 'Cannot export my data',
    status: SupportTicketStatus.Open,
    priority: SupportTicketPriority.Normal,
    precedingTicketId: null,
    createdAt: '2026-08-20T09:00:00Z',
    updatedAt: '2026-08-21T10:00:00Z',
    messageCount: 2,
    lastMessageAt: '2026-08-21T10:00:00Z',
    ownerUserId: '22222222-0000-0000-0000-000000000002',
    ownerEmail: 'filer@example.com',
    ...over,
  };
}

function page(over: Partial<AdminTicketListResponse> = {}): AdminTicketListResponse {
  return { items: [item()], page: 1, pageSize: 25, total: 1, ...over };
}

function mount(path = '/admin/support') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/admin/support" element={<AdminTicketListPage />} />
        <Route path="/admin/support/:ticketId" element={<AdminTicketListPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  apiFetch.mockReset();
  navigate.mockReset();
});

describe('AdminTicketListPage', () => {
  it('lists tickets with the owner email', async () => {
    apiFetch.mockResolvedValue({ ok: true, status: 200, data: page() });
    mount();

    expect(await screen.findByText('Cannot export my data')).toBeInTheDocument();
    expect(screen.getByText(/filer@example\.com/)).toBeInTheDocument();
    expect(screen.getByText(/1 ticket$/)).toBeInTheDocument();
  });

  it('renders the not-authorized state on a 403 (server is the authority)', async () => {
    apiFetch.mockResolvedValue({ ok: false, status: 403, code: 'FORBIDDEN', message: 'no' });
    mount();

    expect(await screen.findByText(/don't have access/i)).toBeInTheDocument();
    // No ticket table when forbidden.
    expect(screen.queryByText('Cannot export my data')).toBeNull();
  });

  it('paginates: Next requests the following page', async () => {
    apiFetch.mockResolvedValue({
      ok: true,
      status: 200,
      data: page({ total: 30, pageSize: 25 }), // 2 pages
    });
    mount();

    await screen.findByText('Cannot export my data');
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));

    await waitFor(() =>
      expect(apiFetch).toHaveBeenCalledWith(expect.stringContaining('page=2')),
    );
  });

  it('filters by status: selecting Solved re-queries with the status param', async () => {
    apiFetch.mockResolvedValue({ ok: true, status: 200, data: page() });
    mount();

    await screen.findByText('Cannot export my data');
    // Native <select> filter — select the Solved option by label.
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /filter by status/i }), 'Solved');

    await waitFor(() =>
      expect(apiFetch).toHaveBeenCalledWith(
        expect.stringContaining(`status=${SupportTicketStatus.Solved}`),
      ),
    );
  });

  it('opens the thread sheet on row click', async () => {
    apiFetch.mockResolvedValue({ ok: true, status: 200, data: page() });
    mount();

    await userEvent.click(await screen.findByText('Cannot export my data'));
    expect(navigate).toHaveBeenCalledWith(
      '/admin/support/11111111-0000-0000-0000-000000000001',
    );
  });
});
