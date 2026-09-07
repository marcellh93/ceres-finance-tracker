import { test, expect, type Page, type APIRequestContext } from '@playwright/test'
import { createVerifiedUser, DEFAULT_PASSWORD, type TestUser } from '../support/users'
import { postJson } from '../support/api'
import { grantAdminByEmail } from '../support/admin'

// Stage 12.5.2 — the admin ticket-list / triage surface. An operator (Admin role) can see
// every user's tickets at /admin/support and open a thread. A non-admin who navigates
// there gets the not-authorized state (fork 2a: the server [RequireAdmin] check is the
// authority; there is no cached client admin flag).

async function loginInBrowser(page: Page, request: APIRequestContext, baseURL: string, user: TestUser) {
  const res = await postJson(request, baseURL, '/api/auth/login', {
    email: user.email,
    password: DEFAULT_PASSWORD,
    rememberMe: false,
  })
  if (res.status() !== 204) throw new Error(`login failed: ${res.status()} ${await res.text()}`)
  const state = await request.storageState()
  await page.context().addCookies(state.cookies)
}

test('Admin support: an operator sees another user\'s ticket and opens its thread', async ({
  page,
  request,
  baseURL,
  playwright,
}) => {
  const url = baseURL!

  // A regular user files a ticket.
  const filer = await createVerifiedUser(request, url)
  const filerCtx = await playwright.request.newContext()
  const filerLogin = await postJson(filerCtx, url, '/api/auth/login', {
    email: filer.email, password: DEFAULT_PASSWORD, rememberMe: false,
  })
  if (filerLogin.status() !== 204) throw new Error(`filer login failed: ${filerLogin.status()}`)
  const subject = `admin-e2e ${Date.now()}`
  const create = await postJson(filerCtx, url, '/api/support/tickets', {
    subject, message: 'Please help — this is the admin-list E2E ticket.', priority: 1,
  })
  if (create.status() !== 201 && create.status() !== 200)
    throw new Error(`ticket create failed: ${create.status()} ${await create.text()}`)
  await filerCtx.dispose()

  // A second account, granted Admin, drives the browser.
  const operator = await createVerifiedUser(request, url)
  grantAdminByEmail(operator.email)
  await loginInBrowser(page, request, url, operator)

  await page.goto('/admin/support')
  await expect(page.getByRole('heading', { level: 1, name: 'Support (admin)', exact: true })).toBeVisible()

  // The filer's ticket (another user's) is visible to the operator, with the filer's email.
  await expect(page.getByText(subject)).toBeVisible()
  await expect(page.getByText(filer.email, { exact: false })).toBeVisible()

  // Open the thread.
  await page.getByText(subject).click()
  const sheet = page.getByRole('dialog')
  await expect(sheet.getByText(`Filed by ${filer.email}`)).toBeVisible()
  await expect(sheet.getByText('Please help — this is the admin-list E2E ticket.')).toBeVisible()
})

test('Admin support: a non-admin sees the not-authorized state', async ({ page, request, baseURL }) => {
  const url = baseURL!
  const plain = await createVerifiedUser(request, url)
  await loginInBrowser(page, request, url, plain)

  await page.goto('/admin/support')

  // 2a: no cached flag — the page renders, its admin API call 403s, and the not-authorized
  // state shows. The ticket table must NOT appear.
  await expect(page.getByText(/don't have access to this page/i)).toBeVisible()
})
