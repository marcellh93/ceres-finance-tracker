# ADR-0019 — Session Management: User-Configurable Lifetime and IP Controls

**Status:** Accepted (Phase 3)

**Context:**

Financial applications require secure session management, but rigid rules create real UX problems for legitimate users:

- Invalidating sessions on IP change logs users out on mobile devices (IP changes between WiFi and cellular) and with VPNs — both common, legitimate scenarios
- Preventing multi-device use is unnecessarily restrictive
- A user who suspects their account has been compromised needs actionable tools — not just a blanket timeout

At the same time, a financial app must not leave long-lived sessions unguarded. The goal is to give users meaningful control over the security/convenience tradeoff, with clear disclosure of what each choice means, rather than imposing a single policy that is either too restrictive or too permissive.

**Decision:**

Sessions are tracked server-side in a `UserSession` table (one row per active session per device). This enables multi-device support, a user-visible active session list, and per-session revocation.

**Always enforced — not configurable:**
- Session token is regenerated immediately after successful login (prevents session fixation)
- On logout, the session record is marked revoked in the database — clearing the client cookie alone is not sufficient

**User-configurable with risk disclosure:**

1. **Session lifetime** — users choose between:
   - Short session: expires on browser close or after an idle timeout (e.g. 30 minutes)
   - Persistent "remember me": long-lived (e.g. 30 days rolling), implemented as a separate token stored hashed in the database and rotated on each use (issue a new token, invalidate the old one). Raw tokens are never stored.

2. **IP enforcement** — enforced per session, not per user. Each `UserSession` row stores the IP at creation time. When IP enforcement is enabled, each request is validated against that session's own IP — not a single account-wide IP. This means multiple devices with different IPs are fully compatible with enforcement on (each device has its own session anchored to its own creation IP). The only affected case is a single device whose IP changes (mobile switching networks, VPN reconnecting to a different exit node).

3. **IP blocking** — users can block specific IP addresses from the Security settings page. Any request from a blocked IP is rejected immediately and all active sessions from that IP are revoked. This is always active regardless of the IP enforcement toggle. The intended workflow: user reviews the login/logout audit log (which records IP on every event), identifies a suspicious IP, and blocks it. Blocked IPs are stored in a `UserBlockedIp` table.

**Consequences:**

- Users have a meaningful, informed choice over session security rather than a single imposed policy
- Per-session IP enforcement resolves the apparent conflict between IP enforcement and multi-device use — they are orthogonal
- IP blocking gives users a practical incident response tool informed by audit log data
- Requires two new Phase 3 entities: `UserSession` and `UserBlockedIp`
- More implementation complexity than a simple timeout-based approach; each configurable behavior must be explicitly implemented (ASP.NET Core Identity does not provide IP enforcement or IP blocking out of the box)
- TOTP replay prevention and account-level lockout after failed login attempts are complementary security measures documented separately in planning.md Phase 3
