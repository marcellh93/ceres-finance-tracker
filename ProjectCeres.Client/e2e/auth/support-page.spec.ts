import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { postJson } from '../support/api'
import { createVerifiedUser, DEFAULT_PASSWORD, type TestUser } from '../support/users'
import { grantAdminByEmail } from '../support/admin'

/**
 * Stage 12.6 — the /support conversation surface.
 *
 * Drives the real server: a verified user files a ticket, opens its thread in
 * the URL-reflected slide-in sheet, replies, and closes it. The agent-reply leg
 * is API-seeded through the [RequireAdmin] operator endpoint (there is no
 * operator UI this stage — see support/admin.ts for the role-grant seam), then
 * the user's browser is asserted to render it.
 *
 * Unit/Vitest tests mock apiFetch and the router; these prove the wire contract
 * and the URL-reflected sheet against the actual API + React Router.
 */

// Log a verified user in and hand their cookies to the browser context.
async function loginAs(page: Page, request: APIRequestContext, baseURL: string, user: TestUser) {
  const res = await postJson(request, baseURL, '/api/auth/login', {
    email: user.email,
    password: DEFAULT_PASSWORD,
    rememberMe: false,
  })
  if (res.status() !== 204) throw new Error(`login failed: ${res.status()} ${await res.text()}`)
  const state = await request.storageState()
  await page.context().addCookies(state.cookies)
}

test('Support: file a ticket, then open its thread from the list', async ({ page, request, baseURL }) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url)
  await loginAs(page, request, url, user)

  await page.goto('/support')
  await expect(page.getByRole('heading', { level: 1, name: 'Support', exact: true })).toBeVisible()

  // Empty state on a fresh account.
  await expect(page.getByText(/haven't opened any support tickets/i)).toBeVisible()

  // File a ticket through the compose sheet.
  await page.getByRole('button', { name: 'New ticket' }).click()
  await expect(page).toHaveURL(/\/support\/new$/)
  const compose = page.getByRole('dialog')
  await compose.getByLabel('Subject').fill('Export button does nothing')
  await compose.getByLabel('Message').fill('Clicking export produces an empty file.')
  await compose.getByRole('button', { name: 'Open ticket' }).click()

  // The new ticket's thread opens in the sheet (URL reflects the ticket id).
  await expect(page).toHaveURL(/\/support\/[0-9a-f-]{36}$/)
  const thread = page.getByRole('dialog')
  await expect(thread.getByText('Clicking export produces an empty file.')).toBeVisible()
  await expect(thread.getByText(/You ·/)).toBeVisible()

  // Back to the list: the ticket is there, badged Open.
  await page.goto('/support')
  await expect(page.getByText('Export button does nothing')).toBeVisible()
  await expect(page.getByText('Open', { exact: true })).toBeVisible()
})

test('Support: a deep link opens the thread sheet on load', async ({ page, request, baseURL }) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url)
  await loginAs(page, request, url, user)

  // Seed a ticket via the API, then navigate straight to its URL.
  const create = await postJson(request, url, '/api/support/tickets', {
    subject: 'Deep link target',
    message: 'Opened straight from the URL.',
    priority: 1,
  })
  expect(create.status()).toBe(201)
  const { id } = (await create.json()) as { id: string }

  await page.goto(`/support/${id}`)
  const thread = page.getByRole('dialog')
  await expect(thread.getByText('Deep link target')).toBeVisible()
  await expect(thread.getByText('Opened straight from the URL.')).toBeVisible()
})

test('Support: user reply appends to the thread and keeps the ticket Open', async ({ page, request, baseURL }) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url)
  await loginAs(page, request, url, user)

  const create = await postJson(request, url, '/api/support/tickets', {
    subject: 'Reply round-trip',
    message: 'First message.',
    priority: 1,
  })
  const { id } = (await create.json()) as { id: string }

  await page.goto(`/support/${id}`)
  const thread = page.getByRole('dialog')
  await thread.getByRole('textbox').fill('Any update on this?')
  await thread.getByRole('button', { name: 'Send reply' }).click()

  // The reply appears in the thread, and the ticket stays Open (a user reply
  // hands the ball back to the operator; it never advances the status itself).
  await expect(thread.getByText('Any update on this?')).toBeVisible()
  await expect(thread.getByText('Open', { exact: true })).toBeVisible()
})

test('Support: an operator agent reply renders in the user thread', async ({ page, request, baseURL, playwright }) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url)
  await loginAs(page, request, url, user)

  // The user files a ticket.
  const create = await postJson(request, url, '/api/support/tickets', {
    subject: 'Needs an operator answer',
    message: 'Please advise.',
    priority: 1,
  })
  const { id } = (await create.json()) as { id: string }

  // --- Agent-reply leg (no operator UI this stage) --------------------------
  // A second account is granted Admin and posts an agent reply through the
  // [RequireAdmin] operator endpoint. The message is owner-stamped with the
  // TICKET OWNER's id server-side, so it must read back in the USER's thread.
  const operator = await createVerifiedUser(request, url)
  grantAdminByEmail(operator.email)
  const adminCtx = await playwright.request.newContext()
  const adminLogin = await postJson(adminCtx, url, '/api/auth/login', {
    email: operator.email,
    password: DEFAULT_PASSWORD,
    rememberMe: false,
  })
  if (adminLogin.status() !== 204) throw new Error(`admin login failed: ${adminLogin.status()}`)
  const agentReply = await postJson(adminCtx, url, `/api/admin/support/tickets/${id}/messages`, {
    body: 'Thanks — we have a fix rolling out.',
    status: 1, // Pending — waiting on the user
  })
  if (agentReply.status() !== 204)
    throw new Error(`agent reply failed: ${agentReply.status()} ${await agentReply.text()}`)
  await adminCtx.dispose()
  // --------------------------------------------------------------------------

  await page.goto(`/support/${id}`)
  const thread = page.getByRole('dialog')
  await expect(thread.getByText('Thanks — we have a fix rolling out.')).toBeVisible()
  await expect(thread.getByText(/Support ·/)).toBeVisible()
  await expect(thread.getByText('Pending', { exact: true })).toBeVisible()
})

test('Support: closing a ticket hides the composer and offers a follow-up', async ({ page, request, baseURL }) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url)
  await loginAs(page, request, url, user)

  const create = await postJson(request, url, '/api/support/tickets', {
    subject: 'Close me',
    message: 'This can be closed.',
    priority: 1,
  })
  const { id } = (await create.json()) as { id: string }

  await page.goto(`/support/${id}`)
  const thread = page.getByRole('dialog')

  // Close through the confirmation dialog.
  await thread.getByRole('button', { name: 'Close ticket' }).click()
  const confirm = page.getByRole('alertdialog')
  await confirm.getByRole('button', { name: 'Close ticket' }).click()

  // The composer is gone; the closed-state follow-up affordance is shown.
  await expect(thread.getByRole('textbox')).toHaveCount(0)
  await expect(thread.getByText(/this ticket is closed/i)).toBeVisible()
  await expect(thread.getByRole('button', { name: 'Start a follow-up' })).toBeVisible()
  await expect(thread.getByText('Closed', { exact: true })).toBeVisible()
})

test('Support: the thread sheet is full-width and usable at 375px', async ({ page, request, baseURL }) => {
  const url = baseURL!
  await page.setViewportSize({ width: 375, height: 720 })
  const user = await createVerifiedUser(request, url)
  await loginAs(page, request, url, user)

  const create = await postJson(request, url, '/api/support/tickets', {
    subject: 'Mobile thread',
    message: 'Rendered on a narrow viewport.',
    priority: 1,
  })
  const { id } = (await create.json()) as { id: string }

  await page.goto(`/support/${id}`)
  const thread = page.getByRole('dialog')
  await expect(thread.getByText('Rendered on a narrow viewport.')).toBeVisible()

  // The sheet fills (near) the full 375px width rather than the primitive's
  // default 3/4 — the scoped width override the design pass added.
  const box = await thread.boundingBox()
  expect(box).not.toBeNull()
  expect(box!.width).toBeGreaterThan(340)

  // The composer and its Send control are present and reachable at this width
  // (no clipping / overflow). NB: the ≥44px touch-target floor (roadmap
  // responsive rule) is NOT asserted here — the sm Button is h-7 like every
  // other shipped page, and the mobile touch-target bump is a design-system-
  // wide item tracked as an open [ ] for /support (mirrors the open /settings/
  // sessions item). Asserting 44px here would be asserting a state that does
  // not exist yet.
  await expect(thread.getByRole('textbox')).toBeVisible()
  await expect(thread.getByRole('button', { name: 'Send reply' })).toBeVisible()

  // No horizontal overflow of the page body at 375px (roadmap: no overflow ≥320px).
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth <= window.innerWidth + 1,
  )
  expect(overflow).toBe(true)
})
