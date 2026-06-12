import type { APIRequestContext } from '@playwright/test'

// The CSRF request token comes from the GET /api/auth/csrf RESPONSE HEADER
// (X-XSRF-TOKEN). It rotates on login/logout, so re-fetch before each mutating call.
export async function csrfToken(request: APIRequestContext, baseURL: string): Promise<string> {
  const res = await request.get(`${baseURL}/api/auth/csrf`)
  const token = res.headers()['x-xsrf-token']
  if (!token) throw new Error('no X-XSRF-TOKEN header on /api/auth/csrf response')
  return token
}

export async function postJson(
  request: APIRequestContext,
  baseURL: string,
  path: string,
  body: unknown,
) {
  const token = await csrfToken(request, baseURL)
  return request.post(`${baseURL}${path}`, {
    headers: { 'X-XSRF-TOKEN': token, 'Content-Type': 'application/json' },
    data: body,
  })
}
