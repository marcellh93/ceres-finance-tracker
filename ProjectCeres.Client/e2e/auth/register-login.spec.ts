import { test, expect } from '@playwright/test'
import { uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { waitForEmail, linkPath } from '../support/emails'

test('register → verify email → login → dashboard', async ({ page, context }) => {
  const email = uniqueEmail('reglogin')

  // Register through the UI.
  await page.goto('/app/register')
  await expect(page.getByRole('heading', { name: 'Create your account' })).toBeVisible()
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(DEFAULT_PASSWORD)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Check your inbox' })).toBeVisible()

  // Follow the emailed verification link (root-path bug fixed in Task 1 → /app/email-verify).
  const mail = await waitForEmail(email, (e) => e.bodyText.includes('/app/email-verify#token='))
  await page.goto(linkPath(mail))
  await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible()

  // Sign in.
  await page.goto('/app/login')
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(DEFAULT_PASSWORD)
  await page.getByRole('button', { name: 'Sign in' }).click()

  // Landed in the app shell with a session cookie.
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 10_000 })
  const cookies = await context.cookies()
  expect(cookies.some((c) => c.name === '__Host-Session')).toBe(true)
})
