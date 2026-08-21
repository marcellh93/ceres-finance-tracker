import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementClearedToggle } from './MovementClearedToggle';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

let mockFetch: ReturnType<typeof vi.fn>;

// apiFetch bootstraps CSRF via GET /api/auth/csrf before its first
// state-changing call and caches the token module-side. Serve that handshake
// here so the PATCH under test carries a real X-XSRF-TOKEN header — the header
// whose absence caused the 400 this component used to hit.
const CSRF_TOKEN = 'test-xsrf-token';

function csrfHandshakeResponse() {
  return {
    ok: true,
    status: 204,
    headers: { get: (name: string) => (name === 'X-XSRF-TOKEN' ? CSRF_TOKEN : null) },
  };
}

/** The PATCH call recorded by the fetch stub, skipping the CSRF handshake. */
function patchCall() {
  return mockFetch.mock.calls.find(
    (c) => (c[1] as RequestInit | undefined)?.method === 'PATCH',
  );
}

/** Builds a stubbed apiFetch-shaped response for the PATCH under test. */
function patchResponse(ok: boolean) {
  return {
    ok,
    status: ok ? 204 : 400,
    headers: { get: () => null },
    json: async () => null,
  };
}

beforeEach(() => {
  mockFetch = vi.fn(async (url: string, init?: RequestInit) => {
    if (typeof url === 'string' && url.startsWith('/api/auth/csrf')) {
      return csrfHandshakeResponse();
    }
    return patchResponse(init?.method !== undefined);
  });
  global.fetch = mockFetch as unknown as typeof fetch;
});

afterEach(() => {
  vi.resetAllMocks();
});

describe('MovementClearedToggle', () => {
  it('renders Cleared label when isCleared=true', () => {
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={true} />);
    expect(screen.getByText('Cleared')).toBeInTheDocument();
  });

  it('renders Pending label when isCleared=false', () => {
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);
    expect(screen.getByText('Pending')).toBeInTheDocument();
  });

  it('PATCHes the API on click and updates the label', async () => {
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => {
      expect(patchCall()?.[0]).toBe('/api/movements/m1/cleared');
      expect(screen.getByText('Cleared')).toBeInTheDocument();
    });
  });

  // Regression: the component used raw fetch() and sent no X-XSRF-TOKEN, so the
  // global AutoValidateAntiforgeryTokenAttribute rejected every PATCH with 400
  // and the pill silently failed with "Couldn't update status."
  it('sends the X-XSRF-TOKEN header on the PATCH', async () => {
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => {
      const headers = (patchCall()?.[1] as RequestInit).headers as Record<string, string>;
      expect(headers['X-XSRF-TOKEN']).toBe(CSRF_TOKEN);
    });
  });

  it('reverts the label and fires error toast on failed PATCH', async () => {
    const { toast } = await import('sonner');
    mockFetch.mockImplementation(async (url: string) => {
      if (typeof url === 'string' && url.startsWith('/api/auth/csrf')) return csrfHandshakeResponse();
      return patchResponse(false);
    });
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => {
      expect(screen.getByText('Pending')).toBeInTheDocument();
      expect(toast.error).toHaveBeenCalledWith("Couldn't update status.");
    });
  });

  it('sends type=liabilitypayment for LiabilityPayment movements', async () => {
    render(<MovementClearedToggle id="lp1" type="LiabilityPayment" isCleared={false} />);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => {
      const body = JSON.parse((patchCall()?.[1] as RequestInit).body as string);
      expect(body.type).toBe('liabilitypayment');
    });
  });

  it('two rapid clicks settle in the second click direction even if responses arrive out of order', async () => {
    let resolveFirst!: (value: { ok: boolean }) => void;
    let resolveSecond!: (value: { ok: boolean }) => void;
    const responses = [
      new Promise<{ ok: boolean }>((r) => { resolveFirst = r; }),
      new Promise<{ ok: boolean }>((r) => { resolveSecond = r; }),
    ];
    let call = 0;
    // The CSRF handshake must not consume a slot in the ordered PATCH queue.
    global.fetch = vi.fn((url: string) =>
      typeof url === 'string' && url.startsWith('/api/auth/csrf')
        ? Promise.resolve(csrfHandshakeResponse())
        : responses[call++],
    ) as unknown as typeof fetch;

    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);
    const btn = screen.getByRole('button');

    fireEvent.click(btn); // optimistic: true (next = true)
    fireEvent.click(btn); // optimistic: false (next = false, since serverCleared still false)

    resolveSecond({ ok: true });  // second click succeeds → serverCleared stays false
    resolveFirst({ ok: false });  // first click fails → toast, no state change

    await waitFor(() => {
      expect(screen.getByText('Pending')).toBeInTheDocument();
    });
  });
});
