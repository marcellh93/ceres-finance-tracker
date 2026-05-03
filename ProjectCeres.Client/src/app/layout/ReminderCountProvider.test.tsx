import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ReminderCountProvider, useReminderCount } from './ReminderCountProvider';

function CountDisplay() {
  const { count, reminders } = useReminderCount();
  return <div data-testid="count">{count} / {reminders.length}</div>;
}

describe('ReminderCountProvider', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('provides count from /api/recurring-transactions/upcoming?days=0', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      ok: true, json: async () => [{ id: '1' }, { id: '2' }],
    });
    render(<ReminderCountProvider><CountDisplay /></ReminderCountProvider>);
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('2 / 2'));
  });

  it('empty list yields count=0', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      ok: true, json: async () => [],
    });
    render(<ReminderCountProvider><CountDisplay /></ReminderCountProvider>);
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('0 / 0'));
  });
});
