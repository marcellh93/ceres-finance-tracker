import '@testing-library/jest-dom'
import { vi } from 'vitest'

// @testing-library/react's fake-timer detection checks `typeof jest !== 'undefined'`
// and then inspects `setTimeout.clock` (set by @sinonjs/fake-timers, which Vitest
// uses internally). Exposing `vi` as `jest` lets RTL detect Vitest's fake timers
// and advance them correctly inside waitFor, preventing hangs when
// vi.useFakeTimers() is active.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
;(globalThis as any).jest = vi

// matchMedia polyfill for useMediaQuery tests
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {}, // deprecated
    removeListener: () => {}, // deprecated
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => true,
  }),
})

// Recharts uses ResizeObserver to measure ResponsiveContainer — mock it for jsdom
global.ResizeObserver = class ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

// cmdk calls scrollIntoView when navigating items — mock it for jsdom
Element.prototype.scrollIntoView = () => {}

// Recharts also calls getBoundingClientRect for dimensions
Element.prototype.getBoundingClientRect = () => ({
  width: 500,
  height: 300,
  top: 0,
  left: 0,
  bottom: 0,
  right: 0,
  x: 0,
  y: 0,
  toJSON: () => {},
})
