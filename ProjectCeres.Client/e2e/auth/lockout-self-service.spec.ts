import { test, expect } from '@playwright/test'
import { createVerifiedUser, uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { waitForEmail, linkPath } from '../support/emails'

test('10 wrong passwords → lockout email → unlock link → re-login', async ({ page, request, baseURL }) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('lockout'),
    password: DEFAULT_PASSWORD,
  })

  // Drive 10 failed logins through the form. The raised E2E AuthLoginByIp limit is what
  // lets all 10 reach the controller so the lockout TRANSITION (and its email) fires.
  await page.goto('/app/login')
  for (let i = 0; i < 10; i++) {
    await page.locator('#email').fill(user.email)
    await page.locator('#password').fill(`wrong-password-attempt-${i}-long-enough`)
    await page.getByRole('button', { name: 'Sign in' }).click()
    // Wait for the error to render before the next attempt (serial, deterministic).
    await page.waitForTimeout(150)
  }

  // Exactly one unlock email, with the /app-prefixed link (Task 1 fix).
  const mail = await waitForEmail(user.email, (e) => e.bodyText.includes('/app/account/unlock#token='))
  await page.goto(linkPath(mail))
  await expect(page.getByRole('heading', { name: 'Unlock your account' })).toBeVisible()
  await page.getByRole('button', { name: 'Unlock account' }).click()

  await expect(page).toHaveURL(/\/app\/login\?unlocked=1/, { timeout: 10_000 })
  await expect(page.getByText('Your account is unlocked. Sign in to continue.')).toBeVisible()

  // Correct password now works.
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })
})
