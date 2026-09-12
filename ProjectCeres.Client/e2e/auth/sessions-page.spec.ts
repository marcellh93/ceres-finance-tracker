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
  //
  // Stage 12.10 login-dedup revokes a same-device duplicate — keyed on
  // (UserId, UserAgent, IpCreatedAt). Both contexts share the Playwright host IP,
  // so without a DISTINCT User-Agent the second login would dedup the first away
  // and only one session would exist. A different UA makes them genuinely two
  // devices, which is what "another device" means here.
  const other = await playwright.request.newContext({
    extraHTTPHeaders: { 'User-Agent': 'CeresE2E-OtherDevice/1.0 (Linux; test)' },
  })
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

test('Sessions: anchoring another device to its IP flips the row and shows the honest warning', async ({
  page,
  request,
  baseURL,
  playwright,
}) => {
  // Stage 12.5.1 — the real anchor toggle through the UI, driven against the server.
  // Anchor a SECOND (non-current) device: anchoring the current session would sign this
  // test out on its next request (the intended self-lockout), which we cover at the unit
  // and integration layer instead. Here we prove the toggle + warning + row state.
  const url = baseURL!
  const user = await createVerifiedUser(request, url)

  // A distinct User-Agent makes this a genuinely separate device, so Stage 12.10
  // login-dedup (keyed on UserId + UserAgent + IP) does not collapse it into the
  // current session — see the revoke test above for the same reasoning.
  const other = await playwright.request.newContext({
    extraHTTPHeaders: { 'User-Agent': 'CeresE2E-OtherDevice/1.0 (Linux; test)' },
  })
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

  // Both devices show an "Anchor IP" button; act on the OTHER one so the test's
  // own session survives the round-trip. Target it by the distinct User-Agent it
  // logged in with ("Unknown browser on Linux" — the summary of our custom UA),
  // which is unambiguous and does not depend on excluding the current row.
  const otherRow = page.getByRole('listitem').filter({ hasText: 'Unknown browser on Linux' })
  await expect(otherRow).toBeVisible()

  // Its confirm dialog must state the stopgap honestly — it protects against a
  // replayed cookie, not a password sign-in.
  await otherRow.getByRole('button', { name: 'Anchor IP' }).click()

  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByText(/does not block someone who signs in with your password/i)).toBeVisible()
  await dialog.getByRole('button', { name: 'Anchor session' }).click()

  // The row flips to the anchored state: the badge appears and the button now reads "Anchored".
  await expect(page.getByText('Anchored to IP')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Anchored' })).toHaveCount(1)

  await other.dispose()
})

test('Sessions: a blocked address can be unblocked from the Blocked addresses section', async ({
  page,
  request,
  baseURL,
}) => {
  // Stage 12.5.4 — the real block→unblock round trip through the UI. Seed a block on a
  // concrete address via the API (login first stamps the 5-minute reauth window that
  // /api/sessions/block-ip requires), then unblock it in the browser.
  const url = baseURL!
  const user = await createVerifiedUser(request, url)
  const loginRes = await postJson(request, url, '/api/auth/login', {
    email: user.email,
    password: DEFAULT_PASSWORD,
    rememberMe: false,
  })
  if (loginRes.status() !== 204) throw new Error(`login failed: ${loginRes.status()}`)

  const blockedIp = '203.0.113.77'
  const blockRes = await postJson(request, url, '/api/sessions/block-ip', { ipAddress: blockedIp })
  if (blockRes.status() !== 204) throw new Error(`block failed: ${blockRes.status()} ${await blockRes.text()}`)

  const state = await request.storageState()
  await page.context().addCookies(state.cookies)
  await page.goto('/settings/sessions')

  // The Blocked addresses section appears with the seeded address.
  await expect(page.getByRole('heading', { level: 1, name: 'Active sessions', exact: true })).toBeVisible()
  await expect(page.getByText('Blocked addresses')).toBeVisible()
  await expect(page.getByText(blockedIp)).toBeVisible()

  // Unblock it: confirm in the dialog.
  await page.getByRole('button', { name: 'Unblock' }).click()
  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toBeVisible()
  // Match the dialog title as a literal string (SessionsPage renders `Unblock {ip}?`),
  // not a regex built from blockedIp. Building a RegExp from a string while escaping
  // only `.` is incomplete sanitization (CodeQL js/incomplete-sanitization): any other
  // regex metacharacter in the value would pass through unescaped. A literal getByText
  // needs no escaping and expresses the assertion directly.
  await expect(dialog.getByText(`Unblock ${blockedIp}?`, { exact: false })).toBeVisible()
  await dialog.getByRole('button', { name: 'Unblock' }).click()

  // The section disappears once the only block is gone (it renders only when non-empty).
  await expect(page.getByText('Blocked addresses')).toHaveCount(0)
  await expect(page.getByText(blockedIp)).toHaveCount(0)
})

test('Sessions: mobile — action buttons meet the 44px touch target and no horizontal overflow', async ({
  page,
  request,
  baseURL,
}) => {
  // Stage 12.8 Bucket D: at mobile width the per-row action buttons (sm size) must be
  // ≥44px tall via the responsive Button bump, and the page must not overflow horizontally.
  const url = baseURL!
  await page.setViewportSize({ width: 375, height: 720 })
  const user = await createVerifiedUser(request, url)
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

  // The current session's action buttons (Sign out, Anchor IP) are sm-size; on mobile
  // they must be ≥44px tall. A 1px tolerance for sub-pixel rounding.
  for (const name of ['Sign out', 'Anchor IP']) {
    const btn = page.getByRole('button', { name })
    await expect(btn).toBeVisible()
    const box = await btn.boundingBox()
    expect(box, `${name} button has a box`).not.toBeNull()
    expect(box!.height, `${name} button ≥44px tall on mobile`).toBeGreaterThanOrEqual(43.5)
  }

  // No horizontal overflow of the page body at 375px (roadmap: no overflow ≥320px).
  const noOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth <= window.innerWidth + 1,
  )
  expect(noOverflow).toBe(true)
})

test('Sessions: repeated logins from one browser show a single row (dedup)', async ({
  page,
  request,
  baseURL,
}) => {
  // Stage 12.10 dedup, through the browser (converts the owed manual pass): two logins from
  // the SAME context (same UA + IP) collapse to one live session per device, so the list shows
  // exactly one row. The integration layer pins the DB behaviour (SessionLoginDedupTests); this
  // proves it surfaces as a single row in the real UI.
  const url = baseURL!
  const user = await createVerifiedUser(request, url)

  // Log in twice through the same request context — same device from the server's view.
  for (const attempt of [1, 2]) {
    const res = await postJson(request, url, '/api/auth/login', {
      email: user.email,
      password: DEFAULT_PASSWORD,
      rememberMe: false,
    })
    if (res.status() !== 204) throw new Error(`login ${attempt} failed: ${res.status()}`)
  }

  const state = await request.storageState()
  await page.context().addCookies(state.cookies)
  await page.goto('/settings/sessions')
  await expect(page.getByRole('heading', { level: 1, name: 'Active sessions', exact: true })).toBeVisible()

  // Exactly one session row: the second login deduped the first (same device).
  // The card title counts rows, and the current-device markers appear exactly once
  // (a second live row would title "2 active sessions" and add another "This device").
  await expect(page.getByText('1 active session')).toBeVisible()
  await expect(page.getByText('2 active sessions')).toHaveCount(0)
  await expect(page.getByText('This device')).toHaveCount(1)
  // Exactly one row-level action set (the current session shows Sign out + Anchor IP).
  await expect(page.getByRole('button', { name: 'Sign out' })).toHaveCount(1)
})
