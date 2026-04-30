import '@testing-library/jest-dom'

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
