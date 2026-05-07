import { render, act } from '@testing-library/react';
import { useRef } from 'react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useScrollRestoration } from './use-scroll-restoration';

const STORAGE_KEY = 'ceres:scroll-positions';

function Harness({ onScrollEl }: { onScrollEl: (el: HTMLElement | null) => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useScrollRestoration(ref);
  return (
    <div
      ref={(el) => {
        ref.current = el;
        onScrollEl(el);
      }}
      data-testid="scroller"
      style={{ height: 200, overflow: 'auto' }}
    >
      <div style={{ height: 1000 }} />
    </div>
  );
}

function NavTo({ to, label }: { to: string; label: string }) {
  const navigate = useNavigate();
  return (
    <button onClick={() => navigate(to)}>{label}</button>
  );
}

function GoBack() {
  const navigate = useNavigate();
  return <button onClick={() => navigate(-1)}>Go back</button>;
}

function App() {
  return (
    <Routes>
      <Route
        path="/"
        element={
          <>
            <Harness onScrollEl={() => {}} />
            <NavTo to="/other" label="Go other" />
            <NavTo to="/" label="Go home" />
          </>
        }
      />
      <Route
        path="/other"
        element={
          <>
            <Harness onScrollEl={() => {}} />
            <NavTo to="/" label="Go home" />
            <GoBack />
          </>
        }
      />
    </Routes>
  );
}

beforeEach(() => {
  sessionStorage.clear();
});

afterEach(() => {
  sessionStorage.clear();
});

describe('useScrollRestoration', () => {
  it('persists scrollTop on scroll under the pathname key', async () => {
    let scroller: HTMLElement | null = null;
    render(
      <MemoryRouter initialEntries={['/']}>
        <Harness onScrollEl={(el) => { scroller = el; }} />
      </MemoryRouter>,
    );
    expect(scroller).not.toBeNull();
    if (!scroller) return;

    // Wait for the initial restore guard (set inside a requestAnimationFrame) to clear
    // before firing user scroll events.
    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

    act(() => {
      (scroller as HTMLElement).scrollTop = 250;
      (scroller as HTMLElement).dispatchEvent(new Event('scroll'));
    });

    const stored = JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? '{}');
    expect(stored).toEqual({ '/': 250 });
  });

  it('resets scroll to 0 on PUSH navigation', async () => {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify({ '/other': 400 }));
    const { getByText, getByTestId } = render(
      <MemoryRouter initialEntries={['/']}>
        <App />
      </MemoryRouter>,
    );
    const scroller = getByTestId('scroller') as HTMLElement;
    scroller.scrollTop = 0;

    await act(async () => {
      getByText('Go other').click();
    });

    // On PUSH, scrollTop is reset to 0 even though /other had a saved 400.
    const newScroller = getByTestId('scroller') as HTMLElement;
    expect(newScroller.scrollTop).toBe(0);
  });

  it('restores saved scrollTop on POP (browser back) navigation', async () => {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify({ '/': 320 }));

    // Start with both / and /other in the history, currently on /other.
    // navigate(-1) drives a real POP through MemoryRouter's history.
    const { getByText, getByTestId } = render(
      <MemoryRouter initialEntries={['/', '/other']} initialIndex={1}>
        <App />
      </MemoryRouter>,
    );

    await act(async () => {
      getByText('Go back').click();
    });
    // Yield twice for the layout effect to mount and the restore-rAF to fire.
    await act(async () => {
      await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
      await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
    });

    const scroller = getByTestId('scroller') as HTMLElement;
    expect(scroller.scrollTop).toBe(320);
  });
});
