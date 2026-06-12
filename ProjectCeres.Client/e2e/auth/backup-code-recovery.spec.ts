import { test, expect } from '@playwright/test'
import { createVerifiedUser, uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { postJson } from '../support/api'
import { totpFromUri, code } from '../support/totp'

test('lost device → backup code login → dashboard; reuse rejected', async ({ page, request, baseURL }) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url, {
    email: uniqueEmail('backup'),
    password: DEFAULT_PASSWORD,
  })

  // Log in via API to get a session that satisfies [RequireRecentAuth], then enrol TOTP.
  const login = await postJson(request, url, '/api/auth/login', {
    email: user.email, password: user.password, rememberMe: false,
  })
  expect(login.status()).toBe(204)

  const enroll = await postJson(request, url, '/api/auth/mfa/enroll', {})
  expect(enroll.status()).toBe(200)
  const { otpAuthUri } = await enroll.json()
  const totp = totpFromUri(otpAuthUri)

  const verify = await postJson(request, url, '/api/auth/mfa/enroll/verify', { code: code(totp) })
  expect(verify.status()).toBe(200)
  const { backupCodes } = await verify.json()
  expect(Array.isArray(backupCodes)).toBe(true)
  expect(backupCodes.length).toBeGreaterThan(0)
  const backupCode: string = backupCodes[0]

  // Fresh browser session: log in via UI → TOTP challenge → switch to backup code.
  await page.context().clearCookies()
  await page.goto('/app/login')
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/login\/totp/, { timeout: 10_000 })

  await page.getByRole('button', { name: 'Lost your device? Use a backup code' }).click()
  await page.locator('#backup-code').fill(backupCode)
  await page.getByRole('button', { name: 'Verify' }).click()
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })

  // Edge: the same backup code is single-use — a second login with it is rejected.
  await page.context().clearCookies()
  await page.goto('/app/login')
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/login\/totp/, { timeout: 10_000 })
  await page.getByRole('button', { name: 'Lost your device? Use a backup code' }).click()
  await page.locator('#backup-code').fill(backupCode)
  await page.getByRole('button', { name: 'Verify' }).click()
  // Stays on the TOTP page with an error; does NOT reach the dashboard.
  await expect(page).toHaveURL(/\/app\/login\/totp/)
  await expect(page.getByText('Invalid code')).toBeVisible()
})
