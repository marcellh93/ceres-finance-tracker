import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementClearedToggle } from './MovementClearedToggle';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

beforeEach(() => {
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
    mockFetch.mockResolvedValue({ ok: true });
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith('/api/movements/m1/cleared', expect.objectContaining({ method: 'PATCH' }));
      expect(screen.getByText('Cleared')).toBeInTheDocument();
    });
  });

  it('reverts the label and fires error toast on failed PATCH', async () => {
    const { toast } = await import('sonner');
    mockFetch.mockResolvedValue({ ok: false });
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => {
      expect(screen.getByText('Pending')).toBeInTheDocument();
      expect(toast.error).toHaveBeenCalledWith("Couldn't update status.");
    });
  });

  it('sends type=liabilitypayment for LiabilityPayment movements', async () => {
    mockFetch.mockResolvedValue({ ok: true });
    render(<MovementClearedToggle id="lp1" type="LiabilityPayment" isCleared={false} />);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => {
      const call = mockFetch.mock.calls[0];
      const body = JSON.parse(call[1].body);
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
    global.fetch = vi.fn(() => responses[call++]) as unknown as typeof fetch;

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
