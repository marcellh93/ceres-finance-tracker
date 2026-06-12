import { URI, TOTP } from 'otpauth'

// Parse the real otpauth:// URI returned by POST /api/auth/mfa/enroll so the helper
// uses the server's actual algorithm/digits/period rather than hardcoding them.
export function totpFromUri(otpauthUri: string): TOTP {
  const otp = URI.parse(otpauthUri)
  if (!(otp instanceof TOTP)) throw new Error(`URI resolved to HOTP, expected TOTP: ${otpauthUri}`)
  return otp
}

export function code(totp: TOTP): string {
  return totp.generate()
}

// Wait until the current 30s window rolls to a fresh code distinct from `previous`
// (the server replay-guard rejects reuse within a window).
export async function nextDistinctCode(totp: TOTP, previous: string): Promise<string> {
  for (let i = 0; i < 35; i++) {
    const c = totp.generate()
    if (c !== previous) return c
    await new Promise((r) => setTimeout(r, 1000))
  }
  throw new Error('TOTP code did not roll within 35s')
}
