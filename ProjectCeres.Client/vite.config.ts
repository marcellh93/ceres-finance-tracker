import path from 'path'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { visualizer } from 'rollup-plugin-visualizer'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'https://localhost:7001',
        // The .NET dev server uses a self-signed certificate. Without
        // `secure: false`, the proxy intermittently fails the first request
        // with `Failed to fetch` (TLS verification rejects the cert before
        // the kernel-level TCP socket is reused). `changeOrigin` rewrites
        // the Host header so Kestrel routes the proxied request correctly.
        secure: false,
        changeOrigin: true,
      },
    },
    strictPort: true,
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
          // vendor-overlay: sonner (toasts), cmdk (command palette), next-themes.
          // Small but were pulled into the auto-named chunk along with the rest.
          if (id.includes('node_modules/sonner') ||
              id.includes('node_modules/cmdk') ||
              id.includes('node_modules/next-themes'))
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
  },
})
