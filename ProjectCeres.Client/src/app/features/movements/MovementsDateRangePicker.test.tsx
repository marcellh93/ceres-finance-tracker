import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementsDateRangePicker } from './MovementsDateRangePicker';
import { formatRangeLabel, readDraftFromParams, toIsoDate } from './movements-date-range-utils';

// Mock the Calendar primitive so we can drive it deterministically without
// fighting react-day-picker's keyboard navigation in jsdom.
vi.mock('@/components/ui/calendar', () => {
  return {
    Calendar: ({ selected, onSelect }: { selected: unknown; onSelect: (r: unknown) => void }) => (
      <div data-testid="mock-calendar">
        <span data-testid="mock-calendar-selected">{JSON.stringify(selected ?? null)}</span>
        <button
          type="button"
          onClick={() =>
            onSelect({
              from: new Date(2026, 2, 5), // 2026-03-05
              to: new Date(2026, 2, 12),  // 2026-03-12
            })
          }
        >
          pick-range
        </button>
      </div>
    ),
  };
});

beforeEach(() => {
  // Pin "today" so This-month-style presets are deterministic. Today is
  // 2026-05-01 per project context.
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(new Date(2026, 4, 1, 12, 0, 0));
});

afterEach(() => {
  vi.useRealTimers();
  vi.resetAllMocks();
});

function LocationSpy({ onChange }: { onChange: (search: string) => void }) {
  const loc = useLocation();
  onChange(loc.search);
  return null;
}

function renderPicker(initial = '/movements') {
  let captured = initial.includes('?') ? '?' + initial.split('?')[1] : '';
  render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route
          path="/movements"
          element={
            <>
              <MovementsDateRangePicker />
              <LocationSpy onChange={(s) => { captured = s; }} />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return () => captured;
}

describe('MovementsDateRangePicker — pure helpers', () => {
  it('toIsoDate produces local yyyy-MM-dd (no timezone shift)', () => {
    expect(toIsoDate(new Date(2026, 0, 9))).toBe('2026-01-09');
    expect(toIsoDate(new Date(2026, 11, 31))).toBe('2026-12-31');
  });

  it('readDraftFromParams returns undefined when both empty', () => {
    expect(readDraftFromParams(new URLSearchParams())).toBeUndefined();
  });

  it('readDraftFromParams parses both bounds', () => {
    const r = readDraftFromParams(new URLSearchParams('from=2026-04-01&to=2026-04-30'));
    expect(toIsoDate(r!.from!)).toBe('2026-04-01');
    expect(toIsoDate(r!.to!)).toBe('2026-04-30');
  });

  it('formatRangeLabel covers all four states', () => {
    expect(formatRangeLabel(undefined, 'DD/MM/YYYY')).toBe('Any date');
    expect(formatRangeLabel({ from: new Date(2026, 3, 1), to: undefined }, 'DD/MM/YYYY')).toBe('From 01/04/2026');
    expect(formatRangeLabel({ from: undefined, to: new Date(2026, 3, 30) }, 'DD/MM/YYYY')).toBe('Until 30/04/2026');
    expect(formatRangeLabel({ from: new Date(2026, 3, 1), to: new Date(2026, 3, 30) }, 'DD/MM/YYYY')).toBe('01/04/2026 – 30/04/2026');
  });
});

describe('MovementsDateRangePicker — UI', () => {
  it('renders "Any date" when no from/to in URL', () => {
    renderPicker('/movements');
    expect(screen.getByRole('button', { name: /date range/i })).toHaveTextContent(/any date/i);
  });

  it('renders the range label when both are set', () => {
    renderPicker('/movements?from=2026-04-01&to=2026-04-30');
    expect(screen.getByRole('button', { name: /date range/i })).toHaveTextContent('01/04/2026 – 30/04/2026');
  });

  it('clicking "This month" preset writes from+to atomically and resets page', async () => {
    const getSearch = renderPicker('/movements?page=3');
    fireEvent.click(screen.getByRole('button', { name: /date range/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'This month' }));
    await waitFor(() => {
      const s = getSearch();
      expect(s).toContain('from=2026-05-01');
      expect(s).toContain('to=2026-05-31');
      expect(s).not.toContain('page=');
    });
  });

  it('manual calendar selection + Apply writes both URL params atomically', async () => {
    const getSearch = renderPicker('/movements');
    fireEvent.click(screen.getByRole('button', { name: /date range/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'pick-range' }));
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
    await waitFor(() => {
      const s = getSearch();
      expect(s).toContain('from=2026-03-05');
      expect(s).toContain('to=2026-03-12');
    });
  });

  it('Clear button removes both URL params and page', async () => {
    const getSearch = renderPicker('/movements?from=2026-04-01&to=2026-04-30&page=2');
    fireEvent.click(screen.getByRole('button', { name: /date range/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'Clear' }));
    await waitFor(() => {
      const s = getSearch();
      expect(s).not.toContain('from=');
      expect(s).not.toContain('to=');
      expect(s).not.toContain('page=');
    });
  });

  it('"All time" preset clears both params', async () => {
    const getSearch = renderPicker('/movements?from=2026-04-01&to=2026-04-30');
    fireEvent.click(screen.getByRole('button', { name: /date range/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'All time' }));
    await waitFor(() => {
      const s = getSearch();
      expect(s).not.toContain('from=');
      expect(s).not.toContain('to=');
    });
  });
});
