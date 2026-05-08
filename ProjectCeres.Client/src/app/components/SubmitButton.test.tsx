import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SubmitButton } from './SubmitButton';

describe('SubmitButton', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders the idle label initially', () => {
    render(<SubmitButton onClick={async () => {}}>Save</SubmitButton>);
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
  });

  it('shows loading label and aria-busy while the promise is in flight', async () => {
    let resolve!: () => void;
    const onClick = vi.fn(() => new Promise<void>((r) => { resolve = r; }));
    render(<SubmitButton onClick={onClick}>Save</SubmitButton>);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(screen.getByText('Saving…')).toBeInTheDocument());
    expect(screen.getByRole('button')).toHaveAttribute('aria-busy', 'true');

    resolve();
  });

  it('transitions to success then back to idle after successDuration', async () => {
    const onClick = vi.fn(() => Promise.resolve());
    render(<SubmitButton onClick={onClick} successDuration={800}>Save</SubmitButton>);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(screen.getByText('Saved')).toBeInTheDocument());

    await act(async () => {
      vi.advanceTimersByTime(800);
    });

    await waitFor(() => expect(screen.getByText('Save')).toBeInTheDocument());
  });

  it('transitions to error and stays clickable on rejection', async () => {
    const onClick = vi.fn(() => Promise.reject(new Error('boom')));
    render(<SubmitButton onClick={onClick}>Save</SubmitButton>);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(screen.getByText('Try again')).toBeInTheDocument());
    expect(screen.getByRole('button')).not.toBeDisabled();
  });

  it('clicking from error state retries', async () => {
    let attempt = 0;
    const onClick = vi.fn(() => {
      attempt += 1;
      return attempt === 1 ? Promise.reject(new Error('first fails')) : Promise.resolve();
    });
    render(<SubmitButton onClick={onClick}>Save</SubmitButton>);

    fireEvent.click(screen.getByRole('button'));
    await waitFor(() => expect(screen.getByText('Try again')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button'));
    await waitFor(() => expect(screen.getByText('Saved')).toBeInTheDocument());

    expect(onClick).toHaveBeenCalledTimes(2);
  });

  it('double-click during loading is a no-op', async () => {
    const onClick = vi.fn(() => new Promise<void>(() => {})); // never resolves
    render(<SubmitButton onClick={onClick}>Save</SubmitButton>);

    const btn = screen.getByRole('button');
    fireEvent.click(btn);
    fireEvent.click(btn);

    expect(onClick).toHaveBeenCalledTimes(1);
  });
});
