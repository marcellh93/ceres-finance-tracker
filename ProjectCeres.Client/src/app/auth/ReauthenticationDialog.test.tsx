// ReauthenticationDialog.test.tsx — render with a mocked auth context + mocked fetch.
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ReauthenticationDialog } from './ReauthenticationDialog';

// Mock the auth context hook the dialog reads.
vi.mock('./auth-context', () => ({ useAuth: vi.fn() }));
import { useAuth } from './auth-context';

beforeEach(() => {
  vi.restoreAllMocks();
  global.fetch = vi.fn(async (url: string) =>
    url.includes('/api/auth/csrf')
      ? ({ headers: new Headers({ 'X-XSRF-TOKEN': 't' }) } as unknown as Response)
      : ({ status: 204 } as Response));
});

it('renders the password field for a non-MFA user', () => {
  (useAuth as unknown as ReturnType<typeof vi.fn>).mockReturnValue({ user: { twoFactorEnabled: false } });
  render(<ReauthenticationDialog open onSuccess={() => {}} onCancel={() => {}} />);
  expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
  expect(screen.queryByLabelText(/code/i)).not.toBeInTheDocument();
});

it('renders the TOTP field for an MFA user', () => {
  (useAuth as unknown as ReturnType<typeof vi.fn>).mockReturnValue({ user: { twoFactorEnabled: true } });
  render(<ReauthenticationDialog open onSuccess={() => {}} onCancel={() => {}} />);
  expect(screen.getByLabelText(/code/i)).toBeInTheDocument();
});

it('calls onSuccess after a 204 reauth', async () => {
  (useAuth as unknown as ReturnType<typeof vi.fn>).mockReturnValue({ user: { twoFactorEnabled: false } });
  const onSuccess = vi.fn();
  render(<ReauthenticationDialog open onSuccess={onSuccess} onCancel={() => {}} />);
  await userEvent.type(screen.getByLabelText(/password/i), 'pw');
  await userEvent.click(screen.getByRole('button', { name: /confirm/i }));
  await waitFor(() => expect(onSuccess).toHaveBeenCalled());
});

it('shows the error envelope message on 401 and does not call onSuccess', async () => {
  (useAuth as unknown as ReturnType<typeof vi.fn>).mockReturnValue({ user: { twoFactorEnabled: false } });
  (global.fetch as ReturnType<typeof vi.fn>).mockImplementation(async (url: string) =>
    url.includes('/api/auth/csrf')
      ? ({ headers: new Headers({ 'X-XSRF-TOKEN': 't' }) } as unknown as Response)
      : ({ status: 401, json: async () => ({ error: { code: 'INVALID_REAUTH', message: 'Password is incorrect.' } }) } as Response));
  const onSuccess = vi.fn();
  render(<ReauthenticationDialog open onSuccess={onSuccess} onCancel={() => {}} />);
  await userEvent.type(screen.getByLabelText(/password/i), 'wrong');
  await userEvent.click(screen.getByRole('button', { name: /confirm/i }));
  expect(await screen.findByText(/password is incorrect/i)).toBeInTheDocument();
  expect(onSuccess).not.toHaveBeenCalled();
});
