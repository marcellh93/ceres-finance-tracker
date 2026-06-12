import { test, expect } from '@playwright/test'
import { createVerifiedUser, uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { waitForEmail, linkPath } from '../support/emails'

test('forgot password → reset link → new password → login', async ({ page, request, baseURL }) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('pwreset'),
    password: DEFAULT_PASSWORD,
  })
  const newPassword = 'a different long passphrase 9-11-x'

  await page.goto('/app/password-reset')
  await expect(page.getByRole('heading', { name: 'Reset your password' })).toBeVisible()
  await page.locator('#email').fill(user.email)
  await page.getByRole('button', { name: 'Send reset link' }).click()
  await expect(page.getByRole('heading', { name: 'Check your inbox' })).toBeVisible()

  const mail = await waitForEmail(user.email, (e) => e.bodyText.includes('/app/password-reset#token='))
  await page.goto(linkPath(mail))
  await expect(page.getByRole('heading', { name: 'Choose a new password' })).toBeVisible()
  await page.locator('#new-password').fill(newPassword)
  await page.locator('#confirm-password').fill(newPassword)
  await page.getByRole('button', { name: 'Reset password' }).click()

  // Redirects to /login?reset=1 with a toast.
  await expect(page).toHaveURL(/\/app\/login\?reset=1/, { timeout: 10_000 })
  await expect(page.getByText('Password reset. Sign in with your new password.')).toBeVisible()

  // The new password works.
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(newPassword)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })
})
