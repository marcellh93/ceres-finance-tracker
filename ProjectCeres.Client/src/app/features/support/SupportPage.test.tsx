import { render, screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SupportPage } from './SupportPage';
import {
  SupportMessageAuthor,
  SupportTicketPriority,
  SupportTicketStatus,
  type SupportTicketListItemDto,
  type SupportTicketThreadDto,
} from './support-api';

const { toastError, toastSuccess } = vi.hoisted(() => ({
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}));
vi.mock('sonner', () => ({ toast: { error: toastError, success: toastSuccess } }));

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

function listItem(over: Partial<SupportTicketListItemDto> = {}): SupportTicketListItemDto {
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
    ...over,
  };
}

function thread(over: Partial<SupportTicketThreadDto> = {}): SupportTicketThreadDto {
  return {
    id: '11111111-0000-0000-0000-000000000001',
    subject: 'Cannot export my data',
    status: SupportTicketStatus.Open,
    priority: SupportTicketPriority.Normal,
    precedingTicketId: null,
    createdAt: '2026-08-20T09:00:00Z',
    updatedAt: '2026-08-21T10:00:00Z',
    messages: [
      {
        id: 'm1',
        authorRole: SupportMessageAuthor.User,
        body: 'The export button does nothing.',
        createdAt: '2026-08-20T09:00:00Z',
        attachments: [],
      },
      {
        id: 'm2',
        authorRole: SupportMessageAuthor.Agent,
        body: 'Thanks — we are looking into it.',
        createdAt: '2026-08-21T10:00:00Z',
        attachments: [],
      },
    ],
    ...over,
  };
}

/** Mock global fetch (used by useApi) to answer by URL. */
function mockFetch(handlers: Record<string, unknown>) {
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) => {
      const key = Object.keys(handlers).find((k) => url.includes(k));
      const body = key ? handlers[key] : [];
      return Promise.resolve(
        new Response(JSON.stringify(body), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      );
    }),
  );
}

function renderAt(path: string) {
  // Mirror the three App.tsx routes so useParams resolves :ticketId / 'new'.
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/support" element={<SupportPage />} />
        <Route path="/support/new" element={<SupportPage />} />
        <Route path="/support/:ticketId" element={<SupportPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.unstubAllGlobals();
});

describe('SupportPage list', () => {
  it('renders the caller tickets with a status badge and message count', async () => {
    mockFetch({ '/api/support/tickets': [listItem()] });
    renderAt('/support');

    expect(await screen.findByText('Cannot export my data')).toBeDefined();
    expect(screen.getByText('Open')).toBeDefined();
    expect(screen.getByText(/2 messages/)).toBeDefined();
    expect(screen.getByRole('heading', { name: '1 ticket' })).toBeDefined();
  });

  it('shows an empty state with a first-ticket CTA when there are none', async () => {
    mockFetch({ '/api/support/tickets': [] });
    renderAt('/support');

    expect(
      await screen.findByText(/haven't opened any support tickets/i),
    ).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: /open your first ticket/i }));
    expect(navigate).toHaveBeenCalledWith('/support/new');
  });

  it('renders the error state when the list fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response('nope', { status: 500 }))),
    );
    renderAt('/support');
    expect(await screen.findByRole('button', { name: /try again/i })).toBeDefined();
  });

  it.each([
    [SupportTicketStatus.Open, 'Open'],
    [SupportTicketStatus.Pending, 'Pending'],
    [SupportTicketStatus.OnHold, 'On hold'],
    [SupportTicketStatus.Solved, 'Solved'],
    [SupportTicketStatus.Closed, 'Closed'],
  ])('renders the %s status label', async (status, label) => {
    mockFetch({ '/api/support/tickets': [listItem({ status })] });
    renderAt('/support');
    expect(await screen.findByText(label)).toBeDefined();
  });
});

describe('SupportPage thread sheet', () => {
  it('opens the thread sheet from a deep link and shows both sides of the conversation', async () => {
    mockFetch({
      '/api/support/tickets/11111111-0000-0000-0000-000000000001': thread(),
      '/api/support/tickets': [listItem()],
    });
    renderAt('/support/11111111-0000-0000-0000-000000000001');

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('The export button does nothing.')).toBeDefined();
    expect(within(dialog).getByText('Thanks — we are looking into it.')).toBeDefined();
    // The agent message is labelled Support, the user's is labelled You.
    expect(within(dialog).getByText(/Support ·/)).toBeDefined();
    expect(within(dialog).getByText(/You ·/)).toBeDefined();
  });

  it('posts a reply and refetches the thread', async () => {
    mockFetch({
      '/api/support/tickets/11111111-0000-0000-0000-000000000001': thread(),
      '/api/support/tickets': [listItem()],
    });
    apiFetch.mockResolvedValue({ ok: true, status: 200, data: { id: 'reply-msg-1' } });
    renderAt('/support/11111111-0000-0000-0000-000000000001');

    const dialog = await screen.findByRole('dialog');
    const box = within(dialog).getByRole('textbox');
    await userEvent.type(box, 'Any update?');
    await userEvent.click(within(dialog).getByRole('button', { name: /send reply/i }));

    await waitFor(() =>
      expect(apiFetch).toHaveBeenCalledWith(
        '/api/support/tickets/11111111-0000-0000-0000-000000000001/messages',
        { method: 'POST', body: { body: 'Any update?' } },
      ),
    );
  });

  it('renders each message attachment as a download link', async () => {
    mockFetch({
      '/api/support/tickets/11111111-0000-0000-0000-000000000001': thread({
        messages: [
          {
            id: 'm1',
            authorRole: SupportMessageAuthor.User,
            body: 'See the screenshot.',
            createdAt: '2026-08-20T09:00:00Z',
            attachments: [
              {
                id: 'att-1',
                fileName: 'screenshot.png',
                contentType: 'image/png',
                fileSizeBytes: 1024,
                uploadedAt: '2026-08-20T09:00:00Z',
              },
            ],
          },
        ],
      }),
      '/api/support/tickets': [listItem()],
    });
    renderAt('/support/11111111-0000-0000-0000-000000000001');

    const dialog = await screen.findByRole('dialog');
    const link = within(dialog).getByRole('link', { name: /screenshot\.png/ });
    expect(link.getAttribute('href')).toBe('/api/attachments/support/att-1');
  });

  it('uploads a buffered file to the new message after a reply posts', async () => {
    mockFetch({
      '/api/support/tickets/11111111-0000-0000-0000-000000000001': thread(),
      '/api/support/tickets': [listItem()],
    });
    // Reply POST returns the message id; the attachment POST returns the dto.
    apiFetch.mockImplementation((url: string) =>
      url.endsWith('/attachments')
        ? Promise.resolve({ ok: true, status: 200, data: { id: 'att-x' } })
        : Promise.resolve({ ok: true, status: 200, data: { id: 'reply-msg-9' } }),
    );
    renderAt('/support/11111111-0000-0000-0000-000000000001');

    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByRole('textbox'), 'Here it is');

    const file = new File(['data'], 'log.txt', { type: 'text/plain' });
    const input = dialog.querySelector('input[type="file"]') as HTMLInputElement;
    await userEvent.upload(input, file);

    await userEvent.click(within(dialog).getByRole('button', { name: /send reply/i }));

    await waitFor(() =>
      expect(apiFetch).toHaveBeenCalledWith(
        '/api/support/messages/reply-msg-9/attachments',
        expect.objectContaining({ method: 'POST' }),
      ),
    );
  });

  it('lets the user close an open ticket after confirming', async () => {
    mockFetch({
      '/api/support/tickets/11111111-0000-0000-0000-000000000001': thread(),
      '/api/support/tickets': [listItem()],
    });
    apiFetch.mockResolvedValue({ ok: true, status: 204, data: null });
    renderAt('/support/11111111-0000-0000-0000-000000000001');

    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: /^close ticket$/i }));

    // A confirmation dialog appears; confirming fires the close POST.
    const confirm = await screen.findByRole('alertdialog');
    await userEvent.click(within(confirm).getByRole('button', { name: /close ticket/i }));

    await waitFor(() =>
      expect(apiFetch).toHaveBeenCalledWith(
        '/api/support/tickets/11111111-0000-0000-0000-000000000001/close',
        { method: 'POST' },
      ),
    );
  });

  it('hides the composer on a Closed ticket and offers a follow-up instead', async () => {
    mockFetch({
      '/api/support/tickets/11111111-0000-0000-0000-000000000001': thread({
        status: SupportTicketStatus.Closed,
      }),
      '/api/support/tickets': [listItem({ status: SupportTicketStatus.Closed })],
    });
    renderAt('/support/11111111-0000-0000-0000-000000000001');

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByRole('textbox')).toBeNull();
    expect(within(dialog).getByText(/this ticket is closed/i)).toBeDefined();
    await userEvent.click(within(dialog).getByRole('button', { name: /start a follow-up/i }));
    expect(navigate).toHaveBeenCalledWith(
      '/support/new',
      expect.objectContaining({
        state: expect.objectContaining({
          precedingTicketId: '11111111-0000-0000-0000-000000000001',
        }),
      }),
    );
  });
});

describe('SupportPage new-ticket sheet', () => {
  it('opens the compose sheet at /support/new and creates a ticket', async () => {
    mockFetch({ '/api/support/tickets': [] });
    apiFetch.mockResolvedValue({
      ok: true,
      status: 201,
      data: {
        id: '22222222-0000-0000-0000-000000000002',
        firstMessageId: '33333333-0000-0000-0000-000000000003',
      },
    });
    renderAt('/support/new');

    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText('Subject'), 'Billing question');
    await userEvent.type(within(dialog).getByLabelText('Message'), 'How do I get a receipt?');
    await userEvent.click(within(dialog).getByRole('button', { name: /open ticket/i }));

    await waitFor(() =>
      expect(apiFetch).toHaveBeenCalledWith(
        '/api/support/tickets',
        expect.objectContaining({
          method: 'POST',
          body: expect.objectContaining({
            subject: 'Billing question',
            message: 'How do I get a receipt?',
            priority: SupportTicketPriority.Normal,
          }),
        }),
      ),
    );
    expect(navigate).toHaveBeenCalledWith('/support/22222222-0000-0000-0000-000000000002');
  });
});
