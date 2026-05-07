import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DataTransition } from './DataTransition';

function setupMatchMedia(matches: boolean) {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
}

describe('DataTransition', () => {
  beforeEach(() => {
    setupMatchMedia(false);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders the skeleton slot when state is "skeleton"', () => {
    render(
      <DataTransition
        state="skeleton"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    expect(screen.getByTestId('sk')).toBeInTheDocument();
  });

  it('renders the data slot when state is "data"', () => {
    render(
      <DataTransition
        state="data"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    expect(screen.getByTestId('data')).toBeInTheDocument();
  });

  it('renders the error slot when state is "error"', () => {
    render(
      <DataTransition
        state="error"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    expect(screen.getByTestId('err')).toBeInTheDocument();
  });

  it('marks the active slot with data-state="active" and uses the motion duration token', () => {
    render(
      <DataTransition
        state="data"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    const active = screen.getByTestId('data').parentElement!;
    expect(active.getAttribute('data-state')).toBe('active');
    expect(active.style.transitionDuration).toBe('var(--motion-duration-base)');
  });

  it('wraps the skeleton slot in a polite status region with default "Loading" label', () => {
    render(
      <DataTransition
        state="skeleton"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    const status = screen.getByRole('status');
    expect(status).toBeInTheDocument();
    expect(status.getAttribute('aria-busy')).toBe('true');
    expect(status.getAttribute('aria-live')).toBe('polite');
    expect(status.getAttribute('aria-label')).toBe('Loading');
    expect(status.contains(screen.getByTestId('sk'))).toBe(true);
  });

  it('uses a custom loadingLabel when provided', () => {
    render(
      <DataTransition
        state="skeleton"
        skeleton={<div>shimmer</div>}
        error={<div>err</div>}
        loadingLabel="Loading transactions"
      >
        <div>data</div>
      </DataTransition>,
    );
    expect(screen.getByRole('status').getAttribute('aria-label')).toBe('Loading transactions');
  });

  it('does not render a status region when state is not skeleton', () => {
    render(
      <DataTransition
        state="data"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('disables transitions when prefers-reduced-motion matches', () => {
    setupMatchMedia(true);
    render(
      <DataTransition
        state="data"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    const root = screen.getByTestId('data').closest('[data-data-transition]')!;
    expect(root.getAttribute('data-reduced-motion')).toBe('true');
  });
});
