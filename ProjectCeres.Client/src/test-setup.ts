import '@testing-library/jest-dom'
import { afterEach, beforeEach, vi } from 'vitest'

// ── Global fetch isolation ────────────────────────────────────────────────────
// Many test files assign `global.fetch = vi.fn()` in a `beforeEach` without
// restoring the original in `afterEach`. Because Vitest runs each test FILE in
// its own worker but runs all TESTS within a file in the same global scope,
// a stale mock from one test bleeds into the next test that runs in the same
// worker. The two confirmed victims were:
//   - features/recurring/RecurringLayout.test.tsx — dialog content missing
//   - features/movements/MovementEdit.test.tsx (Test 1) — fetch() returns undefined
//
// Fix: snapshot the original fetch reference before every test and restore it
// afterward. This is safe for every test: files that set their own mock in
// `beforeEach` will still override after this restore (their `beforeEach` runs
// after this one), and they will find a clean, un-mocked fetch at the start of
// the NEXT test rather than the previous test's leftover mock.
//
// `vi.restoreAllMocks()` covers spies created via `vi.spyOn`.
// `vi.unstubAllGlobals()` covers stubs created via `vi.stubGlobal`.
// The manual save/restore below covers direct assignment (`global.fetch = vi.fn()`),
// which neither of the above APIs touches.
let _originalFetch: typeof fetch

beforeEach(() => {
  _originalFetch = global.fetch
})

afterEach(() => {
  global.fetch = _originalFetch
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

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

// Disable CSS animations and transitions in tests so visual state is
// deterministic by the time render() returns. JSDOM does not animate
// pixels, but transitionend/animationend events still fire on a timer
// — zeroing out durations removes that timing variance.
const noMotionStyle = document.createElement('style')
noMotionStyle.textContent = `
  *, *::before, *::after {
    animation-duration: 0s !important;
    animation-delay: 0s !important;
    transition-duration: 0s !important;
    transition-delay: 0s !important;
    scroll-behavior: auto !important;
  }
`
document.head.appendChild(noMotionStyle)
