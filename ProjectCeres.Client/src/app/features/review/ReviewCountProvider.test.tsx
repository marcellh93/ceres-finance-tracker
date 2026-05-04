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
  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => reconciliation } as Response);
    }
    if (url.endsWith('/transfer-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => transfer } as Response);
    }
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch;
}

describe('ReviewCountProvider', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('provides counts from both endpoints and a derived total', async () => {
    mockCounts(2, 1);
    render(<ReviewCountProvider><CountDisplay /></ReviewCountProvider>);
    await waitFor(() => expect(screen.getByTestId('r').textContent).toBe('2'));
    expect(screen.getByTestId('t').textContent).toBe('1');
    expect(screen.getByTestId('total').textContent).toBe('3');
    expect(screen.getByTestId('loading').textContent).toBe('0');
  });
});
