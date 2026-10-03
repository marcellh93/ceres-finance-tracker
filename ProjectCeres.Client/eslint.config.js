import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
    rules: {
      // Ban dangerouslySetInnerHTML (the XSS vector security-model.md § CSP calls out).
      // Done with no-restricted-syntax rather than eslint-plugin-react/no-danger to
      // avoid pulling a large plugin in for a single rule. The one legitimate use
      // (shadcn's chart primitive, static CSS from a typed config) is allowed via the
      // file-scoped override below.
      'no-restricted-syntax': [
        'error',
        {
          selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']",
          message:
            'dangerouslySetInnerHTML is an XSS vector (security-model.md § CSP). If you have a ' +
            'verified-safe static-content case, add a file-scoped exception in eslint.config.js ' +
            'with a comment explaining why, as chart.tsx does.',
        },
      ],
    },
  },
  {
    // Exception: shadcn's chart primitive injects a <style> built entirely from a
    // typed ChartConfig (no user/network input) — a verified-safe static-CSS use,
    // covered by CSP style-src. See docs/security-model.md § Phase 3 CSP.
    files: ['src/components/ui/chart.tsx'],
    rules: { 'no-restricted-syntax': 'off' },
  },
])
