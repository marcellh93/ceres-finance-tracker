import { test, expect } from '@playwright/test'
import { createVerifiedUser, uniqueEmail, DEFAULT_PASSWORD } from '../support/users'
import { waitForEmail, linkPath } from '../support/emails'

// Stage 12.8: click a REAL emailed email-change link end-to-end. The Vitest cases
// drive the confirm/revoke pages directly; this is the only coverage that follows the
// path a user takes — request from /app/security, then land on the page via the link
// in the email. A fresh login stamps the 5-minute reauth window (AuthController), so
// the request form is not gated by the reauth dialog here; that dialog has its own
// coverage. The token rides in the URL fragment and never reaches the server.

async function login(page: import('@playwright/test').Page, email: string, password: string) {
  await page.goto('/login')
  await page.locator('#email').fill(email)
  await page.locator('#password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/^https?:\/\/[^/]+\/?$/, { timeout: 10_000 })
}

// Fill the /app/security change form and submit. The reauth window is fresh from the
// login moments earlier, so the "Confirm your identity" dialog does not appear.
async function requestEmailChange(page: import('@playwright/test').Page, newEmail: string) {
  await page.goto('/app/security')
  await page.getByRole('button', { name: 'Change email address' }).click()
  await page.locator('#new-email').fill(newEmail)
  await page.getByRole('button', { name: 'Send confirmation link' }).click()
  // The section flips to the "sent" confirmation once the request succeeds.
  await expect(page.getByText('Confirm your new address')).toBeVisible({ timeout: 10_000 })
}

test('change email → confirm link → new address signs in, old one does not', async ({
  page,
  request,
  baseURL,
}) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('emailchg'),
    password: DEFAULT_PASSWORD,
  })
  const newEmail = uniqueEmail('emailchg-new')

  await login(page, user.email, user.password)
  await requestEmailChange(page, newEmail)

  // The confirm link goes to the NEW address.
  const mail = await waitForEmail(newEmail, (e) =>
    e.bodyText.includes('/email-change/confirm#token='),
  )
  await page.goto(linkPath(mail))
  await expect(page.getByRole('heading', { name: 'Email address changed' })).toBeVisible({
    timeout: 10_000,
  })

  // The new address now signs in; the old one no longer does.
  await login(page, newEmail, user.password)

  await page.goto('/login')
  await page.locator('#email').fill(user.email)
  await page.locator('#password').fill(user.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByText('Email or password is incorrect.')).toBeVisible({ timeout: 10_000 })
})

test('change email → revoke link → change is cancelled, old address still signs in', async ({
  page,
  request,
  baseURL,
}) => {
  const user = await createVerifiedUser(request, baseURL!, {
    email: uniqueEmail('emailrev'),
    password: DEFAULT_PASSWORD,
  })
  const newEmail = uniqueEmail('emailrev-new')

  await login(page, user.email, user.password)
  await requestEmailChange(page, newEmail)

  // The revoke link goes to the OLD address, alongside the heads-up notice.
  const mail = await waitForEmail(user.email, (e) =>
    e.bodyText.includes('/email-change/revoke#token='),
  )
  await page.goto(linkPath(mail))
  await expect(page.getByRole('heading', { name: 'Email change cancelled' })).toBeVisible({
    timeout: 10_000,
  })

  // The change was aborted: the old address still signs in.
  await login(page, user.email, user.password)
})
