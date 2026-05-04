import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ReviewCountProvider, useReviewCount } from './ReviewCountProvider';

function CountDisplay() {
  const { reconciliationCount, transferCount, total, loading } = useReviewCount();
  return (
    <div>
      <span data-testid="r">{reconciliationCount}</span>
      <span data-testid="t">{transferCount}</span>
      <span data-testid="total">{total}</span>
      <span data-testid="loading">{loading ? '1' : '0'}</span>
    </div>
  );
}

function mockCounts(reconciliation: number, transfer: number) {
  vi.stubGlobal('fetch', vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => reconciliation } as Response);
    }
    if (url.endsWith('/transfer-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => transfer } as Response);
    }
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch);
}

describe('ReviewCountProvider', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('provides counts from both endpoints and a derived total', async () => {
    mockCounts(2, 1);
    render(<ReviewCountProvider><CountDisplay /></ReviewCountProvider>);
    await waitFor(() => expect(screen.getByTestId('r').textContent).toBe('2'));
    expect(screen.getByTestId('t').textContent).toBe('1');
    expect(screen.getByTestId('total').textContent).toBe('3');
    expect(screen.getByTestId('loading').textContent).toBe('0');
  });

  it('refresh refetches both endpoints', async () => {
    const fetchMock = vi.fn((url: string) => {
      if (url.endsWith('/reconciliation-review/pending/count')) {
        return Promise.resolve({ ok: true, json: async () => 1 } as Response);
      }
      if (url.endsWith('/transfer-review/pending/count')) {
        return Promise.resolve({ ok: true, json: async () => 0 } as Response);
      }
      return Promise.reject(new Error(`Unexpected URL: ${url}`));
    });
    vi.stubGlobal('fetch', fetchMock as typeof fetch);

    function Trigger() {
      const { refresh } = useReviewCount();
      return <button onClick={refresh}>refresh</button>;
    }

    render(
      <ReviewCountProvider>
        <CountDisplay />
        <Trigger />
      </ReviewCountProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('total').textContent).toBe('1'));
    const before = fetchMock.mock.calls.length;

    screen.getByText('refresh').click();
    // Both endpoints should re-fire — at least 2 more calls than before.
    await waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThan(before + 1));
  });

  it('treats an errored single endpoint as count=0; other still works', async () => {
    vi.stubGlobal('fetch', vi.fn((url: string) => {
      if (url.endsWith('/reconciliation-review/pending/count')) {
        return Promise.resolve({ ok: false, status: 500 } as Response);
      }
      if (url.endsWith('/transfer-review/pending/count')) {
        return Promise.resolve({ ok: true, json: async () => 4 } as Response);
      }
      return Promise.reject(new Error(`Unexpected URL: ${url}`));
    }) as typeof fetch);

    render(<ReviewCountProvider><CountDisplay /></ReviewCountProvider>);
    await waitFor(() => expect(screen.getByTestId('loading').textContent).toBe('0'));
    expect(screen.getByTestId('r').textContent).toBe('0');
    expect(screen.getByTestId('t').textContent).toBe('4');
    expect(screen.getByTestId('total').textContent).toBe('4');
  });

  it('loading is true while either fetch is in flight', async () => {
    let resolveRecon: (v: Response) => void;
    vi.stubGlobal('fetch', vi.fn((url: string) => {
      if (url.endsWith('/reconciliation-review/pending/count')) {
        return new Promise<Response>((r) => { resolveRecon = r; });
      }
      return Promise.resolve({ ok: true, json: async () => 0 } as Response);
    }) as typeof fetch);

    render(<ReviewCountProvider><CountDisplay /></ReviewCountProvider>);
    // Transfer endpoint settles fast; reconciliation is held open → loading stays true.
    await waitFor(() => expect(screen.getByTestId('t').textContent).toBe('0'));
    expect(screen.getByTestId('loading').textContent).toBe('1');

    resolveRecon!({ ok: true, json: async () => 0 } as Response);
    await waitFor(() => expect(screen.getByTestId('loading').textContent).toBe('0'));
  });

  it('refresh propagates: a downstream consumer reads the new value after refresh', async () => {
    let recon = 1;
    vi.stubGlobal('fetch', vi.fn((url: string) => {
      if (url.endsWith('/reconciliation-review/pending/count')) {
        return Promise.resolve({ ok: true, json: async () => recon } as Response);
      }
      return Promise.resolve({ ok: true, json: async () => 0 } as Response);
    }) as typeof fetch);

    function Trigger() {
      const { refresh } = useReviewCount();
      return <button onClick={refresh}>refresh</button>;
    }

    render(
      <ReviewCountProvider>
        <CountDisplay />
        <Trigger />
      </ReviewCountProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('total').textContent).toBe('1'));
    recon = 0;                         // simulate the row being mutated server-side
    screen.getByText('refresh').click();
    await waitFor(() => expect(screen.getByTestId('total').textContent).toBe('0'));
  });
});
