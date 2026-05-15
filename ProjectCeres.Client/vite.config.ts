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
          if (id.includes('node_modules/react') || id.includes('node_modules/react-dom')) return 'vendor-react'
          if (id.includes('node_modules/recharts'))    return 'vendor-charts'
          if (id.includes('node_modules/i18next') || id.includes('node_modules/react-i18next') ||
              id.includes('node_modules/i18next-browser-languagedetector'))
            return 'vendor-i18n'
          if (id.includes('node_modules/lucide-react') || id.includes('node_modules/clsx') ||
              id.includes('node_modules/tailwind-merge') || id.includes('node_modules/class-variance-authority'))
            return 'vendor-ui'
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
