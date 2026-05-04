import type { CSSProperties, ReactNode } from 'react';
import { useEffect, useRef, useState } from 'react';
import { useMediaQuery } from '../lib/use-media-query';

export type DataTransitionState = 'skeleton' | 'data' | 'error';

interface DataTransitionProps {
  state: DataTransitionState;
  skeleton: ReactNode;
  error: ReactNode;
  children: ReactNode;
}

const TRANSITION_MS = 180;

/**
 * Cross-fades between three slots (skeleton / error / data) using the
 * design-system motion tokens. The previous slot stays mounted for one
 * transition cycle so the swap is a visual fade rather than a pop.
 *
 * Respects prefers-reduced-motion: when matched, the transition collapses
 * to an instant swap.
 */
export function DataTransition({
  state,
  skeleton,
  error,
  children,
}: DataTransitionProps) {
  const reducedMotion = useMediaQuery('(prefers-reduced-motion: reduce)');
  const [previousState, setPreviousState] = useState<DataTransitionState | null>(null);
  const prevRef = useRef(state);

  useEffect(() => {
    if (prevRef.current === state) return;
    if (reducedMotion) {
      prevRef.current = state;
      setPreviousState(null);
      return;
    }
    setPreviousState(prevRef.current);
    prevRef.current = state;
    const timer = setTimeout(() => setPreviousState(null), TRANSITION_MS);
    return () => clearTimeout(timer);
  }, [state, reducedMotion]);

  function slotFor(s: DataTransitionState): ReactNode {
    if (s === 'skeleton') return skeleton;
    if (s === 'error') return error;
    return children;
  }

  const transitionStyle: CSSProperties = reducedMotion
    ? {}
    : {
        transitionProperty: 'opacity',
        transitionDuration: 'var(--motion-duration-base)',
        transitionTimingFunction: 'var(--motion-easing-standard)',
      };

  return (
    <div
      data-data-transition
      data-reduced-motion={reducedMotion ? 'true' : 'false'}
      className="relative"
    >
      <div
        data-state="active"
        style={{ ...transitionStyle, opacity: 1 }}
      >
        {slotFor(state)}
      </div>
      {previousState !== null && previousState !== state && (
        <div
          data-state="leaving"
          aria-hidden="true"
          style={{
            ...transitionStyle,
            opacity: 0,
            position: 'absolute',
            inset: 0,
            pointerEvents: 'none',
          }}
        >
          {slotFor(previousState)}
        </div>
      )}
    </div>
  );
}
