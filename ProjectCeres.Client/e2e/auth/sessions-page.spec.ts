import { expect, test } from '@playwright/test'
import { postJson } from '../support/api'
import { createVerifiedUser, DEFAULT_PASSWORD } from '../support/users'

/**
 * Stage 12.1 — /settings/sessions.
 *
 * The unit tests mock the step-up wrapper, so they cannot prove the real
 * thing: that the page survives a `401 REAUTH_REQUIRED` instead of being
 * treated as a sign-out. That is what these run against the real server.
 */

test('Sessions: the page lists the current device and does not sign the user out', async ({
  page,
  request,
  baseURL,
}) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url)

  const loginRes = await postJson(request, url, '/api/auth/login', {
    email: user.email,
    password: DEFAULT_PASSWORD,
    rememberMe: false,
  })
  if (loginRes.status() !== 204)
    throw new Error(`login failed: ${loginRes.status()} ${await loginRes.text()}`)

  const state = await request.storageState()
  await page.context().addCookies(state.cookies)

  await page.goto('/settings/sessions')

  // Signing in stamps the reauth claim, so a visit within the 5-minute window
  // must NOT prompt. Reaching the heading at all proves the page did not treat
  // its own 401 handling as a sign-out and bounce to /login.
  await expect(page.getByRole('heading', { level: 1, name: 'Active sessions', exact: true })).toBeVisible()
  await expect(page).toHaveURL(/\/settings\/sessions$/)

  // The session created by this very test must appear, badged as current.
  await expect(page.getByText('This device')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()

  // The raw User-Agent header must never reach the page — stored values run
  // to 512 characters and would wreck the layout.
  await expect(page.getByText(/AppleWebKit\/|Mozilla\/5\.0/)).toHaveCount(0)
})

test('Sessions: revoking another device removes its row', async ({
  page,
  request,
  baseURL,
  playwright,
}) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url)

  // Two logins for the same account = two sessions. The first belongs to a
  // throwaway context so the browser can revoke it as "another device".
  const other = await playwright.request.newContext()
  const otherLogin = await postJson(other, url, '/api/auth/login', {
    email: user.email,
    password: DEFAULT_PASSWORD,
    rememberMe: false,
  })
  if (otherLogin.status() !== 204) throw new Error(`other login failed: ${otherLogin.status()}`)

  const loginRes = await postJson(request, url, '/api/auth/login', {
    email: user.email,
    password: DEFAULT_PASSWORD,
    rememberMe: false,
  })
  if (loginRes.status() !== 204) throw new Error(`login failed: ${loginRes.status()}`)

  const state = await request.storageState()
  await page.context().addCookies(state.cookies)

  await page.goto('/settings/sessions')
  await expect(page.getByRole('heading', { level: 1, name: 'Active sessions', exact: true })).toBeVisible()

  const revoke = page.getByRole('button', { name: 'Revoke' })
  await expect(revoke).toHaveCount(1)
  await revoke.click()

  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toBeVisible()
  await dialog.getByRole('button', { name: 'Revoke' }).click()

  // The other session is gone; ours survives and still shows the badge.
  await expect(page.getByRole('button', { name: 'Revoke' })).toHaveCount(0)
  await expect(page.getByText('This device')).toBeVisible()

  await other.dispose()
})

test('Sessions: no Block IP action is offered on the current device', async ({
  page,
  request,
  baseURL,
}) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url)
  await postJson(request, url, '/api/auth/login', {
    email: user.email,
    password: DEFAULT_PASSWORD,
    rememberMe: false,
  })
  const state = await request.storageState()
  await page.context().addCookies(state.cookies)

  await page.goto('/settings/sessions')
  await expect(
    page.getByRole('heading', { level: 1, name: 'Active sessions', exact: true }),
  ).toBeVisible()

  // Only the current session exists, so there must be no block affordance at
  // all: blocking your own address 403s every later request and cannot be
  // undone from the app.
  await expect(page.getByText('This device')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Block IP' })).toHaveCount(0)
})
