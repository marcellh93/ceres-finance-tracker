import { test, expect } from '@playwright/test';

// Regression guard for the SPA host stylesheet wiring.
//
// In manifest/production mode the SPA gets its CSS only from the built bundle,
// emitted by the `<link vite-href="~/src/app/main.tsx" rel="stylesheet">` tag in
// Views/App/Index.cshtml. That tag was missing, so the whole SPA shipped unstyled
// in production — invisible in dev, where the Vite dev server injects CSS via JS.
// These assertions fail if the host stops emitting the stylesheet, or if the
// global stylesheet stops applying.

test('SPA host emits a built stylesheet', async ({ page }) => {
  await page.goto('/app/login', { waitUntil: 'networkidle' });
  const stylesheets = page.locator('link[rel="stylesheet"]');
  await expect(stylesheets).not.toHaveCount(0);
});

test('global styles are actually applied on the auth surface', async ({ page }) => {
  await page.goto('/app/login', { waitUntil: 'networkidle' });
  // The primary submit button carries the teal brand background once Tailwind
  // loads; an unstyled <button> renders with the UA default (not teal).
  const submit = page.getByRole('button', { name: /sign in/i });
  const bg = await submit.evaluate((el) => getComputedStyle(el).backgroundColor);
  // The teal --primary token; an unstyled <button> would be transparent / UA-default.
  expect(bg).toMatch(/oklch\(0\.52 0\.11 195/);
});
