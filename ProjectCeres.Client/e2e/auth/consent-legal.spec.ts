import { expect, test } from '@playwright/test'
import { createVerifiedUser, DEFAULT_PASSWORD } from '../support/users'

/**
 * Stage 13 — cookie-consent banner + public legal pages.
 *
 * The consent banner mounts at the app root (App.tsx, above <Routes>), so it
 * is present on any route including the public /login page — no auth needed
 * to exercise the first-visit / accept / reload golden path. /privacy and
 * /legal are rendered via LegalLayout and reachable signed-out.
 */

async function login(page: import('@playwright/test').Page, email: string, password: string) {
  await page.goto('/login')
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/^https?:\/\/[^/]+\/?$/, { timeout: 10_000 })
}

test('Consent: first visit shows the banner; Accept all persists and hides it after reload', async ({
  page,
  context,
}) => {
  await context.clearCookies()
  await page.goto('/login')

  const accept = page.getByRole('button', { name: 'Accept all' })
  const reject = page.getByRole('button', { name: 'Reject all' })
  await expect(accept).toBeVisible()
  await expect(reject).toBeVisible()

  await accept.click()
  await expect(accept).not.toBeVisible()

  await page.reload()
  await expect(page.getByRole('button', { name: 'Accept all' })).not.toBeVisible()
})

test('Legal: /privacy is reachable signed-out and renders the policy', async ({ page, context }) => {
  await context.clearCookies()
  await page.goto('/privacy')

  await expect(page.getByRole('heading', { name: 'What data we collect' })).toBeVisible()
  await expect(page.getByText(/not legally reviewed/i)).toBeVisible()
})

test('Legal: /legal is reachable signed-out and exposes the aviso-legal and cookies sections', async ({
  page,
  context,
}) => {
  await context.clearCookies()
  await page.goto('/legal')

  await expect(page.locator('#aviso-legal')).toBeAttached()
  await expect(page.locator('#cookies')).toBeAttached()
  await expect(page.getByRole('heading', { name: 'Legal notice (Aviso Legal)' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Cookie policy' })).toBeVisible()
})

test('Consent: Settings → Privacy & cookies → Manage cookie preferences re-opens the banner', async ({
  page,
  request,
  baseURL,
}) => {
  const user = await createVerifiedUser(request, baseURL!)
  await login(page, user.email, DEFAULT_PASSWORD)

  // Dismiss the first-visit banner first so the Settings button is a genuine
  // re-open, not a banner that was already showing.
  const accept = page.getByRole('button', { name: 'Accept all' })
  if (await accept.isVisible()) {
    await accept.click()
    await expect(accept).not.toBeVisible()
  }

  await page.goto('/settings')
  await expect(page.getByRole('heading', { name: 'Privacy & cookies' })).toBeVisible()

  await page.getByRole('button', { name: 'Manage cookie preferences' }).click()
  await expect(page.getByRole('button', { name: 'Accept all' })).toBeVisible()
})

test('Consent: 375px viewport — banner buttons meet the 44px touch target, no horizontal overflow', async ({
  page,
  context,
}) => {
  await context.clearCookies()
  await page.setViewportSize({ width: 375, height: 720 })
  await page.goto('/login')

  const accept = page.getByRole('button', { name: 'Accept all' })
  const reject = page.getByRole('button', { name: 'Reject all' })
  await expect(accept).toBeVisible()
  await expect(reject).toBeVisible()

  const acceptBox = await accept.boundingBox()
  const rejectBox = await reject.boundingBox()
  expect(acceptBox!.height, 'Accept all ≥44px tall on mobile').toBeGreaterThanOrEqual(43.5)
  expect(rejectBox!.height, 'Reject all ≥44px tall on mobile').toBeGreaterThanOrEqual(43.5)

  const noOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth <= window.innerWidth + 1,
  )
  expect(noOverflow, 'no horizontal overflow at 375px').toBe(true)
})

test('Consent: 375px — expanded "manage preferences" panel is reachable without horizontal overflow', async ({
  page,
  context,
}) => {
  // Gap previously left [ ]: the default banner's 375px overflow was covered, but
  // the expanded manage-preferences panel (3 toggles + Save) was never exercised.
  await context.clearCookies()
  await page.setViewportSize({ width: 375, height: 720 })
  await page.goto('/login')

  await page.getByRole('button', { name: 'Manage preferences' }).click()

  // All three category toggles are reachable (rendered + visible) on a narrow screen.
  const switches = page.getByRole('switch')
  await expect(switches).toHaveCount(3)
  for (let i = 0; i < 3; i++) {
    await expect(switches.nth(i)).toBeVisible()
  }

  // Save is the manage-mode primary action and must meet the touch target.
  const save = page.getByRole('button', { name: 'Save preferences' })
  await expect(save).toBeVisible()
  const saveBox = await save.boundingBox()
  expect(saveBox!.height, 'Save preferences ≥44px tall on mobile').toBeGreaterThanOrEqual(43.5)

  // The expanded panel must not introduce horizontal overflow.
  const noOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth <= window.innerWidth + 1,
  )
  expect(noOverflow, 'expanded manage panel: no horizontal overflow at 375px').toBe(true)
})

test('Consent: no third-party tracking/analytics script loads before a consent choice', async ({
  page,
  context,
}) => {
  // Gap previously left [ ]: "no tracking scripts before consent" was true by
  // inspection but unasserted. This pins it — a future analytics tag added to the
  // pre-consent load (the exact thing the banner exists to gate) fails this test.
  await context.clearCookies()
  await page.goto('/login')

  // The banner is showing (no choice made yet) — the pre-consent state.
  await expect(page.getByRole('button', { name: 'Accept all' })).toBeVisible()

  // Collect every script src on the page. With no consent given, there must be
  // no cross-origin script (the app's own bundle is same-origin). A third-party
  // analytics/tracking tag would be cross-origin and fail here.
  const crossOriginScripts = await page.evaluate(() => {
    const origin = window.location.origin
    return Array.from(document.querySelectorAll('script[src]'))
      .map((s) => (s as HTMLScriptElement).src)
      .filter((src) => src && !src.startsWith(origin) && !src.startsWith('/'))
  })
  expect(
    crossOriginScripts,
    `no cross-origin scripts before consent; found: ${crossOriginScripts.join(', ')}`,
  ).toEqual([])
})

test('Legal: /privacy and /legal render readably at 375px, no horizontal overflow', async ({
  page,
  context,
}) => {
  await context.clearCookies()
  await page.setViewportSize({ width: 375, height: 720 })

  await page.goto('/privacy')
  await expect(page.getByRole('heading', { name: 'What data we collect' })).toBeVisible()
  let noOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth <= window.innerWidth + 1,
  )
  expect(noOverflow, '/privacy: no horizontal overflow at 375px').toBe(true)

  await page.goto('/legal')
  await expect(page.locator('#cookies')).toBeAttached()
  noOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth <= window.innerWidth + 1,
  )
  expect(noOverflow, '/legal: no horizontal overflow at 375px').toBe(true)
})
