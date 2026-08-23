import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { toast } from 'sonner';
import { MemoryRouter } from 'react-router-dom';
import { SettingsPage } from './SettingsPage';

// SettingsPage links to /settings/sessions, so it needs router context.
// The link is the page's only routing concern; these tests assert settings
// behaviour, not navigation.
function renderPage() {
  return render(
    <MemoryRouter>
      <SettingsPage />
    </MemoryRouter>,
  );
}
import { refetchSettings } from '../../lib/use-settings';
import { primeCsrfToken } from '../../../test/csrf-fetch-mock';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('../../lib/use-settings', () => ({
  refetchSettings: vi.fn().mockResolvedValue(undefined),
}));

let mockFetch: ReturnType<typeof vi.fn>;

const settingsResponse = {
  numberFormat: 'comma_decimal',
  dateFormat: 'DD/MM/YYYY',
  defaultCurrencyCode: 'EUR',
  defaultCurrencySymbol: '€',
  periodStartDay: 1,
};

const currenciesResponse = [
  { id: 1, code: 'EUR', symbol: '€' },
  { id: 2, code: 'USD', symbol: '$' },
];

beforeEach(async () => {
  await primeCsrfToken();
  // Clear call history on module-level mocks (per-file scope, no cross-file race).
  vi.mocked(toast.success).mockClear();
  vi.mocked(toast.error).mockClear();
  vi.mocked(refetchSettings).mockClear();
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/settings') {
      return Promise.resolve({ ok: true, status: 200, json: async () => settingsResponse });
    }
    if (url === '/api/currencies') {
      return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

describe('SettingsPage', () => {
  it('renders skeleton after the delay window when loading is slow', async () => {
    // Make both fetches hang forever for this test.
    mockFetch.mockImplementation(() => new Promise(() => {}));
    renderPage();
    expect(screen.queryByTestId('settings-skeleton')).toBeNull();
    await waitFor(
      () => expect(screen.getByTestId('settings-skeleton')).toBeInTheDocument(),
      { timeout: 500 },
    );
  });

  it('renders the form when data arrives', async () => {
    renderPage();
    await waitFor(() =>
      expect(screen.getByLabelText(/period start day/i)).toHaveValue(1),
    );
  });

  it('renders error block + Retry when GET fails', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/settings') {
        return Promise.resolve({ ok: false, status: 500, json: async () => null });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByText(/couldn't load settings/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
  });

  it('Retry re-fetches after error and renders the form', async () => {
    let attempt = 0;
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/settings') {
        attempt++;
        if (attempt === 1) {
          return Promise.resolve({ ok: false, status: 500, json: async () => null });
        }
        return Promise.resolve({ ok: true, status: 200, json: async () => settingsResponse });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByText(/couldn't load settings/i)).toBeInTheDocument(),
    );
    fireEvent.click(screen.getByRole('button', { name: /retry/i }));
    await waitFor(() =>
      expect(screen.getByLabelText(/period start day/i)).toBeInTheDocument(),
    );
  });

  it('PATCH success shows toast.success and calls refetchSettings', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/settings' && init?.method === 'PATCH') {
        return Promise.resolve({ ok: true, status: 204, json: async () => null });
      }
      if (url === '/api/settings') {
        return Promise.resolve({ ok: true, status: 200, json: async () => settingsResponse });
      }
      if (url === '/api/currencies') {
        return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderPage();
    await screen.findByLabelText(/period start day/i);

    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '15' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Saved.'));
    expect(refetchSettings).toHaveBeenCalledTimes(1);
  });

  it('PATCH failure shows toast.error and Save stays enabled', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (url === '/api/settings' && init?.method === 'PATCH') {
        return Promise.resolve({ ok: false, status: 422, json: async () => null });
      }
      if (url === '/api/settings') {
        return Promise.resolve({ ok: true, status: 200, json: async () => settingsResponse });
      }
      if (url === '/api/currencies') {
        return Promise.resolve({ ok: true, status: 200, json: async () => currenciesResponse });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderPage();
    await screen.findByLabelText(/period start day/i);

    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '15' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't save. Try again."),
    );
    expect(refetchSettings).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });
});
