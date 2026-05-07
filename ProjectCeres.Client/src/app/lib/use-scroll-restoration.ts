import { useEffect, useLayoutEffect, useRef, type RefObject } from 'react';
import { useLocation, useNavigationType } from 'react-router-dom';

const STORAGE_KEY = 'ceres:scroll-positions';
const MAX_RESTORE_FRAMES = 30; // ~500ms at 60fps; covers slow data fetches

/**
 * Restores scrollTop on a container ref across SPA navigations. Restores on
 * POP (browser back/forward), resets to 0 on PUSH/REPLACE. Persists positions
 * in sessionStorage keyed by location.pathname (overridable via getKey).
 *
 * react-router's <ScrollRestoration> only works inside a data router and
 * watches window scroll, neither of which fits this app — the scroll
 * container is <main> inside AppLayout, and we use the legacy <BrowserRouter>.
 *
 * Restore strategy: if the target scrollTop is reachable (scrollHeight tall
 * enough), set it on the next layout. If not (content still mounting / data
 * still loading), retry per animation frame up to ~500ms before giving up.
 * A "restoring" guard prevents the resulting programmatic scroll event from
 * overwriting the saved value with a clamped position.
 */
export function useScrollRestoration(
  containerRef: RefObject<HTMLElement | null>,
  getKey: (pathname: string) => string = (p) => p,
) {
  const location = useLocation();
  const navigationType = useNavigationType();
  const key = getKey(location.pathname);
  const restoringRef = useRef(false);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const onScroll = () => {
      if (restoringRef.current) return;
      const map = readMap();
      map[key] = el.scrollTop;
      writeMap(map);
    };
    el.addEventListener('scroll', onScroll, { passive: true });
    return () => el.removeEventListener('scroll', onScroll);
  }, [key, containerRef]);

  useLayoutEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    if (navigationType !== 'POP') {
      restoringRef.current = true;
      el.scrollTop = 0;
      requestAnimationFrame(() => { restoringRef.current = false; });
      return;
    }

    const target = readMap()[key] ?? 0;
    if (target === 0) {
      restoringRef.current = true;
      el.scrollTop = 0;
      requestAnimationFrame(() => { restoringRef.current = false; });
      return;
    }

    let frame = 0;
    let raf = 0;
    restoringRef.current = true;

    const tryRestore = () => {
      if (!el.isConnected) return;
      el.scrollTop = target;
      const reached = Math.abs(el.scrollTop - target) < 1;
      if (reached || frame >= MAX_RESTORE_FRAMES) {
        // Give the browser one more frame to settle before re-enabling the
        // scroll listener so the final scrollTop doesn't overwrite the save.
        requestAnimationFrame(() => { restoringRef.current = false; });
        return;
      }
      frame += 1;
      raf = requestAnimationFrame(tryRestore);
    };

    raf = requestAnimationFrame(tryRestore);

    return () => {
      cancelAnimationFrame(raf);
      restoringRef.current = false;
    };
  }, [key, navigationType, containerRef]);
}

function readMap(): Record<string, number> {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : {};
  } catch {
    return {};
  }
}

function writeMap(map: Record<string, number>) {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(map));
  } catch {
    // sessionStorage full or disabled — silently no-op
  }
}
