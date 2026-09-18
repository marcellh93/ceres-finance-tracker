import path from 'path'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { visualizer } from 'rollup-plugin-visualizer'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: '/dist/',
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    // HMR over the .NET reverse proxy: the page is served from
    // https://localhost:7081 (Kestrel + Vite.AspNetCore's UseViteDevelopmentServer
    // middleware), so the Vite client must connect its WebSocket to that origin,
    // not to Vite's direct port 5173 (which doesn't terminate TLS). Without these
    // explicit settings Vite auto-derives the WS URL and falls back to
    // wss://localhost:5173 when the auto-derived URL fails — that fallback can
    // never work in a TLS-proxied setup.
    // See https://vite.dev/config/server-options#server-hmr and
    //     https://vite.dev/guide/troubleshooting (HMR fallback section).
    hmr: {
      host: 'localhost',
      protocol: 'wss',
      clientPort: 7081,
    },
    proxy: {
      '/api': {
        target: 'https://localhost:7081',
        // The .NET dev server uses a self-signed certificate. Without
        // `secure: false`, the proxy intermittently fails the first request
        // with `Failed to fetch` (TLS verification rejects the cert before
        // the kernel-level TCP socket is reused). `changeOrigin` rewrites
        // the Host header so Kestrel routes the proxied request correctly.
        secure: false,
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    manifest: true,
    rollupOptions: {
      input: {
        main: path.resolve(__dirname, 'index.html'),
        designSystem: path.resolve(__dirname, 'design-system.html'),
        app: path.resolve(__dirname, 'app.html'),
        // Stage 9.11 — Razor host views resolve these via vite-src in non-Development
        // (manifest mode). Without them the manifest lacks the keys and the SPA host renders blank.
        razorAppEntry: path.resolve(__dirname, 'src/app/main.tsx'),
        razorLayoutEntry: path.resolve(__dirname, 'src/main.tsx'),
      },
      plugins: [
        visualizer({
          filename: 'dist/stats.html',
          template: 'treemap',
          gzipSize: true,
          brotliSize: false,
        }),
      ],
      output: {
        manualChunks(id) {
          if (id.includes('node_modules/react/') || id.includes('node_modules/react-dom/')) return 'vendor-react'
          if (id.includes('node_modules/recharts'))    return 'vendor-charts'
          if (id.includes('node_modules/i18next') || id.includes('node_modules/react-i18next') ||
              id.includes('node_modules/i18next-browser-languagedetector'))
            return 'vendor-i18n'
          if (id.includes('node_modules/lucide-react') || id.includes('node_modules/clsx') ||
              id.includes('node_modules/tailwind-merge') || id.includes('node_modules/class-variance-authority'))
            return 'vendor-ui'
          // vendor-forms: react-hook-form + zod + resolvers adapter. Budget entry
          // added in Task 6 once Login.tsx imports these libs and the chunk is emitted.
          if (id.includes('node_modules/react-hook-form') || id.includes('node_modules/zod') ||
              id.includes('node_modules/@hookform'))
            return 'vendor-forms'
          // vendor-qr: qrcode.react for Phase 4 TOTP setup. Budget entry added in
          // Phase 4 once the TOTP setup page imports this package.
          if (id.includes('node_modules/qrcode.react'))
            return 'vendor-qr'
          // vendor-primitives: @base-ui/react, @floating-ui/*, @radix-ui/*, and the
          // scroll-lock / focus-trap helpers they pull in. Without this rule Rollup
          // auto-names a shared chunk after amount-format.ts (the most-imported
          // source file), causing amount-format-*.js to balloon past its 81 kB gzip
          // budget even though amount-format.ts itself is tiny. Budget added below.
          if (id.includes('node_modules/@base-ui/') ||
              id.includes('node_modules/@floating-ui/') ||
              id.includes('node_modules/@radix-ui/') ||
              id.includes('node_modules/react-remove-scroll') ||
              id.includes('node_modules/react-style-singleton') ||
              id.includes('node_modules/use-sidecar') ||
              id.includes('node_modules/use-callback-ref') ||
              id.includes('node_modules/aria-hidden') ||
              id.includes('node_modules/get-nonce') ||
              id.includes('node_modules/tslib'))
            return 'vendor-primitives'
          // vendor-dates: date-fns, @date-fns/tz, react-day-picker. Date utilities
          // are imported by calendar, budgets, and recurring features. They were
          // landing in the auto-named amount-format chunk because amount-format.ts
          // is their most-shared neighbour. Budget added below.
          if (id.includes('node_modules/date-fns') ||
              id.includes('node_modules/@date-fns/') ||
              id.includes('node_modules/react-day-picker'))
            return 'vendor-dates'
          // vendor-overlay: sonner (toasts), cmdk (command palette).
          // Small but were pulled into the auto-named chunk along with the rest.
          if (id.includes('node_modules/sonner') ||
              id.includes('node_modules/cmdk'))
            return 'vendor-overlay'
        },
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    environmentOptions: {
      jsdom: {
        // __Host- prefixed cookies require a secure context (HTTPS). Setting
        // the JSDOM URL to https://localhost lets the test environment accept
        // document.cookie writes with the __Host-XSRF name so api-client tests
        // can simulate the CSRF handshake without special-casing cookie logic.
        url: 'https://localhost',
      },
    },
    setupFiles: ['./src/test-setup.ts'],
    // Vitest's default exclude does NOT cover `e2e/` — and that directory
    // contains Playwright specs imported from `@playwright/test`, which fails
    // when picked up by Vitest's runner. Keep them separate (Vitest = unit/
    // integration, Playwright = browser e2e).
    exclude: ['node_modules', 'dist', '.idea', '.git', '.cache', 'e2e/**'],
    // The suite is ~152 test files / ~929 tests across heavy portal-rendered
    // UI (base-ui dropdowns, popovers, dialogs) plus axe-core a11y passes.
    // Vitest defaults to one worker per CPU core, which on an 8-core box
    // saturates the CPU and makes `userEvent.click` / `waitFor` cycles
    // exceed the default 5s per-test budget — flakes show up in the
    // worst-contention slot of any given run (different test each run).
    //
    // Two tuning levers:
    //   1. testTimeout 15_000ms — generous per-test budget so a slow worker
    //      slot doesn't fail a test that's otherwise correct.
    //   2. poolOptions.threads.maxThreads 4 — cap concurrency at 4 workers
    //      regardless of CPU count. Halves contention on 8-core hosts;
    //      no effect on CI runners that already have ≤4 cores.
    //
    // Origin: 2026-05-18 audit after 4 different tests intermittently failed
    // across 3 full-suite runs (QuickAddModal, ImportWizard, LanguageToggle,
    // MovementForm). All passed when run scoped; failure pattern was timing
    // under parallel-worker CPU saturation, not logic bugs.
    testTimeout: 15_000,
    // Vitest 4 lifted poolOptions to top-level. The threads pool key for
    // worker count is `maxWorkers` (not nested under `poolOptions.threads`).
    // See https://vitest.dev/guide/migration#pool-rework
    maxWorkers: 4,
    // Flake policy for the slow 2-core CI runner. Every client-test CI failure
    // (2026-09-10) had all assertions passing and failed only on a timing
    // artifact — a base-ui portal popover not painting within a findBy budget.
    // Retry ONLY those: the condition matches timeout / element-not-found, never
    // an assertion mismatch, so a real regression still fails on the first run.
    // https://vitest.dev/config/#retry
    retry: {
      count: 2,
      condition: /Unable to find|Unable to fire|timed out|timeout/i,
    },
    // Reporters. Default (human) always; on CI, add github-actions (native
    // annotations for failures) and our flaky reporter (§12.19 item 5), which
    // surfaces retried-then-passed tests to the job summary so a CI retry can
    // never silently green a degrading test. Local runs keep the plain default.
    reporters: process.env.CI
      ? ['default', 'github-actions', './vitest.flaky-reporter.ts']
      : ['default'],
    // A stray settings fetch can reject after a test's teardown (the useSettings
    // singleton is seeded+reset in test-setup, but an in-flight resolution can land
    // late under CI load). Filter that specific rejection so it's reported but doesn't
    // fail an otherwise-green run — narrower than the blanket dangerouslyIgnoreUnhandledErrors.
    //
    // NOTE (2026-09-12, corrected 2026-09-18): the `window is not defined` post-teardown
    // error was FIXED at the source — an upstream missing-cleanup bug in input-otp 1.4.2,
    // resolved by the bump to 1.5.0 (be0c743f). It was NOT suppressed here, and the earlier
    // `vi.clearAllTimers()` attempt was inert (it only clears fake timers). Do not re-add a
    // `window is not defined` filter — if that error returns it means a timer is leaking
    // teardown again, which is a real resource leak to fix, not to hide. See
    // docs/testing-flakiness.md § 5.
    // https://vitest.dev/config/#onunhandlederror
    onUnhandledError(error): boolean | void {
      const msg = String(error?.message ?? '');
      if (
        msg.includes('/api/settings') ||
        msg.includes('Failed to parse URL')
      ) {
        return false;
      }
    },
  },
})
