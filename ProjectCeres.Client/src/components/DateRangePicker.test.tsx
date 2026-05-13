import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DateRangePicker } from './DateRangePicker';

vi.mock('@/components/ui/calendar', () => ({
  Calendar: ({ onSelect }: { selected: unknown; onSelect: (r: unknown) => void }) => (
    <div data-testid="mock-calendar">
      <button
        type="button"
        onClick={() => onSelect({ from: new Date(2026, 2, 5), to: new Date(2026, 2, 12) })}
      >
        pick-range
      </button>
    </div>
  ),
}));

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(new Date(2026, 4, 1, 12, 0, 0));
});
afterEach(() => { vi.useRealTimers(); vi.resetAllMocks(); });

function LocationSpy({ onChange }: { onChange: (s: string) => void }) {
  const loc = useLocation();
  onChange(loc.search);
  return null;
}

function renderPicker(initial = '/test', fromKey = 'from', toKey = 'to') {
  let captured = '';
  render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route
          path="/test"
          element={
            <>
              <DateRangePicker fromKey={fromKey} toKey={toKey} />
              <LocationSpy onChange={(s) => { captured = s; }} />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return () => captured;
}

describe('DateRangePicker', () => {
  it('renders "Any date" when no params', () => {
    renderPicker();
    expect(screen.getByRole('button', { name: /date range/i })).toHaveTextContent(/any date/i);
  });

  it('shows formatted range when both params are set', () => {
    renderPicker('/test?from=2026-04-01&to=2026-04-30');
    expect(screen.getByRole('button', { name: /date range/i })).toHaveTextContent('01/04/2026');
  });

  it('This month preset writes correct from/to and clears page', async () => {
    const get = renderPicker('/test?page=3');
    fireEvent.click(screen.getByRole('button', { name: /date range/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'This month' }));
    await waitFor(() => {
      const s = get();
      expect(s).toContain('from=2026-05-01');
      expect(s).toContain('to=2026-05-31');
      expect(s).not.toContain('page=');
    });
  });

  it('custom fromKey/toKey writes to different param names', async () => {
    const get = renderPicker('/test', 'dateFrom', 'dateTo');
    fireEvent.click(screen.getByRole('button', { name: /date range/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'This month' }));
    await waitFor(() => {
      expect(get()).toContain('dateFrom=2026-05-01');
      expect(get()).toContain('dateTo=2026-05-31');
    });
  });

  it('Clear removes both params', async () => {
    const get = renderPicker('/test?from=2026-04-01&to=2026-04-30');
    fireEvent.click(screen.getByRole('button', { name: /date range/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'Clear' }));
    await waitFor(() => {
      expect(get()).not.toContain('from=');
    });
  });
});
