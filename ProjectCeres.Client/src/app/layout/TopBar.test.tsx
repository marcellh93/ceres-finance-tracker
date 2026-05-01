import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TopBar } from './TopBar';

beforeEach(() => {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: true, // pretend we're on desktop
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => true,
    }),
  });
});

describe('TopBar quick-add suppression', () => {
  it('renders the Quick add button on the dashboard route', () => {
    render(
      <MemoryRouter initialEntries={['/']}>
        <TopBar onMenuClick={vi.fn()} />
      </MemoryRouter>,
    );
    expect(screen.getByRole('button', { name: /quick add/i })).toBeInTheDocument();
  });

  it('hides the Quick add button on /movements', () => {
    render(
      <MemoryRouter initialEntries={['/movements']}>
        <TopBar onMenuClick={vi.fn()} />
      </MemoryRouter>,
    );
    expect(screen.queryByRole('button', { name: /quick add/i })).toBeNull();
  });

  it('hides the Quick add button on /movements/new', () => {
    render(
      <MemoryRouter initialEntries={['/movements/new']}>
        <TopBar onMenuClick={vi.fn()} />
      </MemoryRouter>,
    );
    expect(screen.queryByRole('button', { name: /quick add/i })).toBeNull();
  });

  it('hides the Quick add button on /movements/abc/edit', () => {
    render(
      <MemoryRouter initialEntries={['/movements/abc/edit']}>
        <TopBar onMenuClick={vi.fn()} />
      </MemoryRouter>,
    );
    expect(screen.queryByRole('button', { name: /quick add/i })).toBeNull();
  });
});
