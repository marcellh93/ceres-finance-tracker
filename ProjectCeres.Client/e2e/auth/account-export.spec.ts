import { expect, test } from '@playwright/test'
import { createVerifiedUser, DEFAULT_PASSWORD } from '../support/users'

/**
 * Stage 13.8 — /settings/account data export.
 *
 * A fresh login stamps the 5-minute reauth window (AuthController), so the
 * export POST's [RequireRecentAuth] gate is satisfied and the step-up dialog
 * does not appear here. Drives the real POST /api/profile/export (202) and
 * asserts the confirmation toast, the 44px mobile touch target, and no 375px
 * horizontal overflow.
 */

async function login(page: import('@playwright/test').Page, email: string, password: string) {
  await page.goto('/login')
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/^https?:\/\/[^/]+\/?$/, { timeout: 10_000 })
}

test('Account: requesting a data export shows the confirmation toast', async ({ page, request, baseURL }) => {
  const user = await createVerifiedUser(request, baseURL!)
  await login(page, user.email, DEFAULT_PASSWORD)

  await page.goto('/settings/account')
  await expect(page.getByRole('heading', { name: 'Account' })).toBeVisible()

  await page.getByRole('button', { name: /export my data/i }).click()

  // 202 → success toast (we'll email you). Sonner renders the message text.
  await expect(page.getByText(/email you a download link/i)).toBeVisible({ timeout: 10_000 })
})

test('Account: reachable from /settings and second same-day export is rate-limited', async ({ page, request, baseURL }) => {
  const user = await createVerifiedUser(request, baseURL!)
  await login(page, user.email, DEFAULT_PASSWORD)

  // Reachable via the Settings hub (no orphaned route).
  await page.goto('/settings')
  await page.getByRole('link', { name: /manage account/i }).click()
  await expect(page).toHaveURL(/\/settings\/account$/)

  await page.getByRole('button', { name: /export my data/i }).click()
  await expect(page.getByText(/email you a download link/i)).toBeVisible({ timeout: 10_000 })

  // A second request within 24h is rate-limited (1/24h) → daily-limit toast.
  await page.getByRole('button', { name: /export my data/i }).click()
  await expect(page.getByText(/one export per day/i)).toBeVisible({ timeout: 10_000 })
})

test('Account: mobile — export button meets the 44px touch target and no horizontal overflow', async ({ page, request, baseURL }) => {
  const user = await createVerifiedUser(request, baseURL!)
  await login(page, user.email, DEFAULT_PASSWORD)

  await page.setViewportSize({ width: 375, height: 720 })
  await page.goto('/settings/account')

  const btn = page.getByRole('button', { name: /export my data/i })
  const box = await btn.boundingBox()
  expect(box!.height, 'export button ≥44px tall on mobile').toBeGreaterThanOrEqual(43.5)

  const noOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth <= window.innerWidth + 1,
  )
  expect(noOverflow, 'no horizontal overflow at 375px').toBe(true)
})
