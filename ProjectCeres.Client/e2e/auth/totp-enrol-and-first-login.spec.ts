import { test, expect } from '@playwright/test'
import { createVerifiedUser, uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { totpFromUri, code, nextDistinctCode } from '../support/totp'

test('enrol TOTP from Security → sign out → login with TOTP → dashboard', async ({ page, request, baseURL }) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('totp'),
    password: DEFAULT_PASSWORD,
  })

  // Log in (no MFA yet).
  await page.goto('/app/login')
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })

  // Start enrolment; capture the otpauth URI from the enroll API response.
  await page.goto('/app/security')
  const enrollResp = page.waitForResponse((r) => r.url().includes('/api/auth/mfa/enroll') && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Set up two-factor sign-in' }).click()
  const enroll = await enrollResp
  const { otpAuthUri } = await enroll.json()
  const totp = totpFromUri(otpAuthUri)

  // Step 1: enter a fresh code (auto-submits on 6 digits).
  await expect(page.getByRole('heading', { name: 'Scan with your authenticator app' })).toBeVisible()
  const enrollCode = code(totp)
  await page.getByRole('textbox', { name: 'Verification code' }).fill(enrollCode)

  // Step 2: save backup codes, confirm, Done.
  await expect(page.getByRole('heading', { name: 'Save your backup codes' })).toBeVisible()
  await page.getByLabel("I've saved my backup codes somewhere safe").check()
  await page.getByRole('button', { name: 'Done' }).click()

  // Clear the session to force a fresh login.
  await page.context().clearCookies()

  // Re-login → requiresTotp.
  await page.goto('/app/login')
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/login\/totp/, { timeout: 10_000 })
  await expect(page.getByRole('heading', { name: 'Verify your identity' })).toBeVisible()

  // Use a code distinct from the enrolment code (replay guard rejects reuse within a window).
  const loginCode = await nextDistinctCode(totp, enrollCode)
  await page.getByRole('textbox', { name: 'Verification code' }).fill(loginCode)

  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })
})
