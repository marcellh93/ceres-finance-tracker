import { expect, test } from '@playwright/test'
import { createVerifiedUser, uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { waitForEmail, linkPath } from '../support/emails'

/**
 * Stage 13.9 Task 9 Step 5 — /settings/account right-to-erasure.
 *
 * A fresh login stamps the 5-minute reauth window (AuthController), so the
 * erasure POST's [RequireRecentAuth] gate is satisfied and the step-up dialog
 * does not appear here. Drives the real typed-confirm dialog end to end: types
 * "ERASE", waits out the deliberate enable-delay, confirms, then follows the
 * REAL emailed cancel link (GdprErasureInitiated) the way a user actually
 * would — the Vitest suites drive ErasureDialog/ErasureCancel in isolation
 * with mocked responses; this is the only coverage that exercises the whole
 * request→email→cancel-link round trip against the real server.
 */

async function login(page: import('@playwright/test').Page, email: string, password: string) {
  await page.goto('/login')
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/^https?:\/\/[^/]+\/?$/, { timeout: 10_000 })
}

async function openErasureDialog(page: import('@playwright/test').Page) {
  await page.goto('/settings/account')
  await expect(page.getByRole('heading', { name: 'Danger zone' })).toBeVisible()
  await page.getByRole('button', { name: 'Erase my account' }).click()
  await expect(page.getByRole('heading', { name: 'Erase your account permanently' })).toBeVisible()
}

test('erasure: request → confirmation toast → real emailed cancel link unseals the account', async ({
  page,
  request,
  baseURL,
}) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('erasure'),
    password: DEFAULT_PASSWORD,
  })

  await login(page, user.email, user.password)
  await openErasureDialog(page)

  const input = page.getByLabel('Type "ERASE" to confirm')
  await input.fill('ERASE')

  // The dialog's confirm button stays disabled for a deliberate beat after the
  // phrase matches (CONFIRM_ENABLE_DELAY_MS) — this is real friction, not test
  // flakiness, so wait it out rather than force-click a disabled button.
  const confirmButton = page.getByRole('button', { name: 'Erase my account' }).last()
  await expect(confirmButton).toBeEnabled({ timeout: 3_000 })
  await confirmButton.click()

  await expect(page.getByText(/link to cancel within 72 hours/i)).toBeVisible({ timeout: 10_000 })
  // The persistent pending banner survives the toast disappearing.
  await expect(page.getByRole('status').filter({ hasText: /erasure is scheduled/i })).toBeVisible()

  // A second request within the same day is idempotent server-side and the
  // dialog closes — no assertion needed here beyond "the app doesn't error";
  // the dedupe contract itself is pinned by ErasureServiceTests.

  const mail = await waitForEmail(user.email, (e) => e.bodyText.includes('/erasure/cancel#token='))
  await page.goto(linkPath(mail))
  await expect(page.getByRole('heading', { name: 'Erasure cancelled' })).toBeVisible({ timeout: 10_000 })
  await expect(page.getByText(/no longer scheduled for erasure/i)).toBeVisible()

  // The account is genuinely unsealed: a normal login now succeeds again.
  await login(page, user.email, user.password)
})

test('erasure: typed-confirm gate rejects a mistyped phrase and never sends the request', async ({
  page,
  request,
  baseURL,
}) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('erasure-badconfirm'),
    password: DEFAULT_PASSWORD,
  })

  await login(page, user.email, user.password)
  await openErasureDialog(page)

  await page.getByLabel('Type "ERASE" to confirm').fill('erase') // lowercase — case-sensitive gate

  const confirmButton = page.getByRole('button', { name: 'Erase my account' }).last()
  await expect(confirmButton).toBeDisabled()
  await expect(page.getByText(/must match exactly/i)).toBeVisible()

  // Still able to sign in — nothing was sealed.
  await page.getByRole('button', { name: 'Cancel' }).click()
  await login(page, user.email, user.password)
})

test('erasure: mobile — dialog action buttons meet the 44px touch target and no horizontal overflow', async ({
  page,
  request,
  baseURL,
}) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('erasure-mobile'),
    password: DEFAULT_PASSWORD,
  })

  await page.setViewportSize({ width: 375, height: 720 })
  await login(page, user.email, user.password)
  await openErasureDialog(page)

  // AlertDialogContent opens with a zoom-in-95 CSS transform (base-ui's data-open
  // animation) — boundingBox() measured mid-transition reports the scaled-down
  // size, not the settled one, which is a real Playwright/CSS-transition gotcha
  // and not specific to this dialog. Wait for the popup's own animations to finish
  // before measuring, exactly the way a real user's tap lands after the dialog is
  // done animating, never mid-flight.
  const dialogContent = page.locator('[data-slot="alert-dialog-content"]')
  await dialogContent.evaluate((el) => Promise.all(el.getAnimations().map((a) => a.finished)))

  const cancelBox = await page.getByRole('button', { name: 'Cancel' }).boundingBox()
  expect(cancelBox!.height, 'Cancel button ≥44px tall on mobile').toBeGreaterThanOrEqual(43.5)

  const confirmBox = await page.getByRole('button', { name: 'Erase my account' }).last().boundingBox()
  expect(confirmBox!.height, 'destructive confirm button ≥44px tall on mobile').toBeGreaterThanOrEqual(43.5)

  const noOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth <= window.innerWidth + 1,
  )
  expect(noOverflow, 'no horizontal overflow at 375px with the dialog open').toBe(true)
})
