import { test, expect } from '@playwright/test'
import type { APIRequestContext } from '@playwright/test'
import { createVerifiedUser, DEFAULT_PASSWORD } from '../support/users'
import { postJson } from '../support/api'

/**
 * Regression cover for the Movements "cleared" pill.
 *
 * The SPA called the API through raw fetch(), so PATCH /api/movements/{id}/cleared
 * carried no X-XSRF-TOKEN header. The global AutoValidateAntiforgeryTokenAttribute
 * rejected it with 400 before the request reached the controller, and the pill
 * silently reverted with "Couldn't update status."
 *
 * Only a browser test covers this end to end: the Vitest suite stubs fetch (so a
 * missing header is unobservable) and the server WAF strips the antiforgery filter
 * (see WafCollection.cs). This spec drives the real pill against the real pipeline.
 */

async function getJson(request: APIRequestContext, baseURL: string, path: string) {
  const res = await request.get(`${baseURL}${path}`)
  if (!res.ok()) throw new Error(`GET ${path} failed: ${res.status()} ${await res.text()}`)
  return res.json()
}

/** Seeds one account + one pending transaction, returning its description. */
async function seedPendingTransaction(request: APIRequestContext, baseURL: string) {
  const currencies = await getJson(request, baseURL, '/api/currencies')
  const accountTypes = await getJson(request, baseURL, '/api/account-types')
  const currencyId = currencies[0].id
  // Asset type so the account holds a plain expense transaction.
  const assetType =
    accountTypes.find((t: { name: string }) => /asset|bank|cash|checking/i.test(t.name)) ??
    accountTypes[0]

  const today = new Date().toISOString().slice(0, 10)

  const accountRes = await postJson(request, baseURL, '/api/accounts', {
    name: `E2E Cleared ${Date.now()}`,
    accountTypeId: assetType.id,
    currencyId,
    description: null,
    // Zero opening balance: a non-zero one writes a transaction against the
    // fixed Opening Balance category, which this fresh E2E database has no row
    // for. The pending transaction created below is the row under test anyway.
    openingBalance: 0,
    openingBalanceDate: today,
    liabilityRepaymentType: null,
    interestRate: null,
    excludeFromSpendable: false,
  })
  if (accountRes.status() !== 201)
    throw new Error(`create account failed: ${accountRes.status()} ${await accountRes.text()}`)
  const account = await accountRes.json()

  // Categories are seeded at registration by CategorySeedService.
  const categories = await getJson(request, baseURL, '/api/categories/active')
  const expenseCategory =
    categories.find((c: { categoryTypeName: string }) => c.categoryTypeName === 'Expense') ??
    categories[0]

  const description = `E2E pending ${Date.now()}`
  const txRes = await postJson(request, baseURL, '/api/transactions', {
    date: today,
    amount: 12.34,
    accountId: account.id,
    categoryId: expenseCategory.id,
    description,
    isCleared: false, // starts Pending — the pill under test
  })
  if (txRes.status() !== 201)
    throw new Error(`create transaction failed: ${txRes.status()} ${await txRes.text()}`)

  return description
}

test('Movements: clicking the Pending pill clears the movement and survives a reload', async ({
  page,
  request,
  baseURL,
}) => {
  const url = baseURL!
  const user = await createVerifiedUser(request, url)

  // Log in through the API so the browser context and the seeding context share
  // a session; then seed the row this test toggles.
  const loginRes = await postJson(request, url, '/api/auth/login', {
    email: user.email,
    password: DEFAULT_PASSWORD,
    rememberMe: false,
  })
  if (loginRes.status() !== 204)
    throw new Error(`login failed: ${loginRes.status()} ${await loginRes.text()}`)

  const description = await seedPendingTransaction(request, url)

  // Carry the API session cookies into the browser context.
  const state = await request.storageState()
  await page.context().addCookies(state.cookies)

  // Fail loudly on the exact symptom: a rejected PATCH.
  const rejected: string[] = []
  page.on('response', (res) => {
    if (res.url().includes('/cleared') && res.status() >= 400) {
      rejected.push(`${res.request().method()} ${res.url()} → ${res.status()}`)
    }
  })

  await page.goto('/movements')

  const row = page.getByRole('row').filter({ hasText: description })
  await expect(row).toBeVisible({ timeout: 15_000 })

  // The pill starts Pending and is the row's toggle button.
  const pill = row.getByRole('button', { name: /mark as cleared/i })
  await expect(pill).toBeVisible()
  await expect(pill).toHaveText(/Pending/)

  await pill.click()

  // Optimistic flip, then the server must agree — no error toast, no 4xx.
  await expect(row.getByRole('button', { name: /mark as pending/i })).toHaveText(/Cleared/, { timeout: 10_000 })
  await expect(page.getByText("Couldn't update status.")).toHaveCount(0)
  expect(rejected, `the cleared PATCH was rejected: ${rejected.join(', ')}`).toEqual([])

  // Reload proves the change was persisted, not just optimistic UI.
  await page.reload()
  const rowAfter = page.getByRole('row').filter({ hasText: description })
  await expect(rowAfter.getByRole('button', { name: /mark as pending/i })).toHaveText(/Cleared/, { timeout: 15_000 })

  // Toggling back also passes antiforgery.
  await rowAfter.getByRole('button', { name: /mark as pending/i }).click()
  await expect(rowAfter.getByRole('button', { name: /mark as cleared/i })).toHaveText(/Pending/, { timeout: 10_000 })
  expect(rejected, `the un-clear PATCH was rejected: ${rejected.join(', ')}`).toEqual([])
})
