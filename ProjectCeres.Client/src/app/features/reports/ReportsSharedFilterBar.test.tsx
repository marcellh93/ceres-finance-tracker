import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { __resetSettingsForTests } from '../../lib/use-settings';
import { ReportsSharedFilterBar } from './ReportsSharedFilterBar';

const SETTINGS = { numberFormat: 'period_decimal', dateFormat: 'DD/MM/YYYY', defaultCurrencyCode: 'EUR', defaultCurrencySymbol: '€', periodStartDay: 1 };
const ALL = [
  { id: 1, code: 'EUR', symbol: '€' },
  { id: 2, code: 'USD', symbol: '$' },
  { id: 3, code: 'GBP', symbol: '£' },
];

beforeEach(() => {
  __resetSettingsForTests();
  global.fetch = vi.fn().mockImplementation(async (url: string) => {
    const u = String(url);
    const body = u.includes('/api/settings') ? SETTINGS : u.includes('inUse=true') ? ALL.slice(0, 2) : ALL;
    return { ok: true, status: 200, json: async () => body };
  }) as unknown as typeof fetch;
});

function renderBar(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <ReportsSharedFilterBar />
    </MemoryRouter>,
  );
}

describe('ReportsSharedFilterBar currency filter', () => {
  it('shows the default currency when the URL names none, instead of an empty placeholder', async () => {
    renderBar('/reports/net-worth');
    await waitFor(() => expect(screen.getByRole('combobox')).toHaveTextContent('€ EUR'));
  });

  it('shows the currency named in the URL over the default', async () => {
    renderBar('/reports/net-worth?currencyId=2');
    await waitFor(() => expect(screen.getByRole('combobox')).toHaveTextContent('$ USD'));
  });

  it('asks the server only for the currencies the user has accounts in', async () => {
    renderBar('/reports/net-worth');
    await waitFor(() =>
      expect((global.fetch as ReturnType<typeof vi.fn>).mock.calls.map((c) => String(c[0]))).toContain('/api/currencies?inUse=true'),
    );
  });
});
