import type { APIRequestContext } from '@playwright/test'
import { postJson } from './api'
import { waitForEmail, extractToken } from './emails'

export interface TestUser {
  email: string
  password: string
}

export function uniqueEmail(prefix = 'e2e'): string {
  return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}@e2e.local`
}

// 33 chars — satisfies the 15-char runtime floor and Identity's 72-char max.
export const DEFAULT_PASSWORD = 'correct horse battery staple 9-11'

// Register via API, read the verify link from the file sink, confirm via API.
// Uses real server paths — registration is prerequisite setup here, not the
// feature under test, so this is allowed per testing.md § Rules.
export async function createVerifiedUser(
  request: APIRequestContext,
  baseURL: string,
  user: TestUser = { email: uniqueEmail(), password: DEFAULT_PASSWORD },
): Promise<TestUser> {
  const reg = await postJson(request, baseURL, '/api/auth/register', {
    email: user.email,
    password: user.password,
  })
  if (reg.status() !== 204)
    throw new Error(`register failed: ${reg.status()} ${await reg.text()}`)

  const email = await waitForEmail(user.email, (e) =>
    e.bodyText.includes('/email-verify#token='),
  )
  const token = extractToken(email)

  const verify = await postJson(request, baseURL, '/api/auth/email/verify', { token })
  if (verify.status() !== 204)
    throw new Error(`verify failed: ${verify.status()} ${await verify.text()}`)

  return user
}
