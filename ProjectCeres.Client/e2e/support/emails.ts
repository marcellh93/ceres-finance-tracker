import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'

// From ProjectCeres.Client/e2e/support/ → up 3 → repo root → .e2e/emails
const SINK_DIR = path.resolve(__dirname, '../../../.e2e/emails')
const TOKEN_RE = /#token=([A-Za-z0-9_-]{43})/

interface SinkEmail {
  to: string
  subject: string
  bodyText: string
  bodyHtml: string
  sentAtUtc: string
}

async function readAll(): Promise<SinkEmail[]> {
  let files: string[]
  try {
    files = await readdir(SINK_DIR)
  } catch {
    return []
  }
  const json = files.filter((f) => f.endsWith('.json'))
  return Promise.all(
    json.map(
      async (f) => JSON.parse(await readFile(path.join(SINK_DIR, f), 'utf8')) as SinkEmail,
    ),
  )
}

// Poll for the newest email to `to` matching `predicate`. Throws on timeout.
export async function waitForEmail(
  to: string,
  predicate: (e: SinkEmail) => boolean = () => true,
  timeoutMs = 15_000,
): Promise<SinkEmail> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const matches = (await readAll())
      .filter((e) => e.to.toLowerCase() === to.toLowerCase() && predicate(e))
      .sort((a, b) => b.sentAtUtc.localeCompare(a.sentAtUtc))
    if (matches.length > 0) return matches[0]
    await new Promise((r) => setTimeout(r, 250))
  }
  throw new Error(`no sink email to ${to} within ${timeoutMs}ms`)
}

export function extractToken(email: SinkEmail): string {
  const m = TOKEN_RE.exec(email.bodyText)
  if (!m) throw new Error(`no #token= in email body: ${email.bodyText.slice(0, 120)}`)
  return m[1]
}

// Build the SPA path a real user would land on after clicking the emailed link.
export function linkPath(email: SinkEmail): string {
  const m = /(https?:\/\/[^\s"]+#token=[A-Za-z0-9_-]{43})/.exec(email.bodyText)
  if (!m) throw new Error('no fragment URL in email body')
  const u = new URL(m[1])
  return u.pathname + u.hash
}
