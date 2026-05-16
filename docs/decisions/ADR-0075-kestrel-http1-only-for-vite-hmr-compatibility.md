# ADR-0075 — Constrain Kestrel to HTTP/1.1 for Vite HMR WebSocket compatibility

## Status: Accepted (2026-05-16, Stage 9 Phase 1 close-out)

## Context

During Stage 9 development, the Vite HMR WebSocket connection failed in the browser every time
the dev server started, producing "WebSocket closed without opened" in the browser console.
The React client would load correctly but hot-module reloading would not function.

### Symptom diagnosis

A curl-based bisect isolated the failure to the HTTP version negotiated:

```bash
# HTTP/1.1 — upgrade succeeds
curl -sk -i -H "Connection: Upgrade" -H "Upgrade: websocket" \
     -H "Sec-WebSocket-Version: 13" \
     -H "Sec-WebSocket-Key: <base64>" \
     -H "Sec-WebSocket-Protocol: vite-hmr" \
     --http1.1 --max-time 3 "https://localhost:7081/?token=test"
# → HTTP/1.1 101 Switching Protocols  (+ Vite's {"type":"connected"} frame)

# Default protocol — upgrade fails
curl -sk -i -H "Connection: Upgrade" -H "Upgrade: websocket" \
     -H "Sec-WebSocket-Version: 13" \
     -H "Sec-WebSocket-Key: <base64>" \
     -H "Sec-WebSocket-Protocol: vite-hmr" \
     --max-time 3 "https://localhost:7081/?token=test"
# → HTTP/2 200  Content-Type: text/html  (SPA index.html served instead of 101)
```

The second request was picked up by Kestrel as an HTTP/2 CONNECT upgrade (RFC 8441 — the
HTTP/2 WebSocket extension). That CONNECT request was then forwarded through the .NET proxy
to Vite, where Vite's middleware-mode HMR server does not handle it — the request fell through
to the catch-all SPA handler, which served `index.html`.

### Root cause — upstream, unresolved

Vite issue [#1807](https://github.com/vitejs/vite/issues/1807) — "hmr websocket failed when
use server.middlewareMode and HTTP2" — is open and marked "pending triage" with no fix.

The Vite.AspNetCore package ([issue #157](https://github.com/Eptagone/Vite.AspNetCore/issues/157))
routes WebSocket upgrades to Vite's HMR endpoint. Because Vite runs in middleware mode, the WS
server is attached to the HTTP/1.1 upgrade path. HTTP/2 WebSocket requests (RFC 8441 CONNECT)
bypass that attachment and reach the SPA catch-all.

This is not fixable in project-level middleware without reimplementing Vite.AspNetCore's WS
routing layer — which would tie us to Vite internals.

## Decision

Constrain every Kestrel endpoint to HTTP/1.1 by adding `Kestrel:EndpointDefaults:Protocols =
"Http1"` to `appsettings.json`.

The constraint lives in `appsettings.json` — not `appsettings.Development.json` — so it applies
to both the development server and any production deployment. The rationale for this scope is
documented in the Alternatives section below.

## Alternatives considered

### Dev-only HTTP/1.1 (rejected)

Putting the constraint in `appsettings.Development.json` would restore HMR in dev but leave
production running HTTP/2. This creates a dev/prod gap: any future WebSocket consumer (SignalR
for real-time reminders, presence indicators, collaborative features) would work correctly in
dev and silently fail in production for the identical reason that Vite HMR fails today —
HTTP/2 CONNECT requests falling through to the wrong handler. The latent landmine is more
dangerous than the cost of constraining HTTP/2 now.

### Separate ports — SPA on its own port, drop the .NET proxy (rejected)

Running the Vite dev server on a separate port (e.g. 5173) and having the browser talk to it
directly would eliminate the proxy. Rejected because:

- `__Host-` cookies are bound to a specific host + port. Cross-port communication invalidates
  them, breaking the auth cookie design documented in `docs/security-model.md`.
- A cross-origin Vite server requires CORS configuration on the API, which the auth design
  deliberately avoids (same-site cookies + no `Authorization` header).
- This would invalidate `docs/architecture.md`'s single-origin model without an ADR for that
  change — a larger decision than the constraint being decided here.

### Build artifacts to wwwroot, no dev proxy (rejected)

Running `pnpm build` on every change and serving static files from `wwwroot` would eliminate
the dev server entirely. Rejected because:

- Eliminates HMR — the entire pain point is HMR not working, not wanting to live without it.
- Build times (Vite + TypeScript + Tailwind v4) add 10–20 seconds per change in a tight
  iteration loop, which is a large DX regression.
- This was explicitly discussed with the user and rejected during Stage 9 design.

### Disable HMR (rejected)

Setting `server.hmr = false` in `vite.config.ts` would silence the browser error. Rejected by
the user earlier in this session — the goal is working HMR, not suppressed errors.

## Consequences

### HTTP/2 features lost

- **Multiplexing** — multiple concurrent requests over one TCP connection. For this app's
  traffic shape (cookie-based auth, REST JSON, server-rendered React bundle), most page loads
  are already fast on HTTP/1.1 with keep-alive. The marginal improvement from multiplexing is
  not measurable for a single-user personal-finance app.
- **Header compression (HPACK)** — repeated headers (e.g. `Cookie`, `Accept`, `Content-Type`)
  are transmitted verbatim. The auth cookie is large (~400 bytes) but sent on every API call
  regardless; the round-trip budget for this app is dominated by DB latency, not header bytes.
- **Server push** — not used anywhere in the current codebase.

### What stays clean

- HTTP/2 WebSockets (RFC 8441) are not used anywhere in this project. Constraining to HTTP/1.1
  now means when we DO add a WebSocket consumer we start from a clean, explicit decision rather
  than inheriting a fragile workaround.
- Every existing integration test passes unchanged — the protocol constraint does not affect
  cookie handling, session tokens, or rate-limit middleware.

## Revisit when

Either of the following conditions is met:

1. **Vite issue #1807 is closed** and `vite.AspNetCore` ships a version that picks up the fix
   (i.e. CONNECT-style WebSocket upgrades reach Vite's HMR server correctly). Verify with the
   same two-curl bisect above before removing the constraint.

2. **We add a real WebSocket consumer** (SignalR hub, live notifications, etc.) with:
   - Explicit HTTP/2 WS tests asserting the endpoint works over CONNECT.
   - A reviewed plan for the dev/prod cert + proxy layer that accounts for the same ALPN
     negotiation dynamic described above.

In either case, remove `Kestrel:EndpointDefaults:Protocols` from `appsettings.json` (not just
the dev override) and verify the removal does not reintroduce the HMR failure.

## References

- Vite #1807 — HMR WS fails behind HTTP/2 + middleware mode:
  https://github.com/vitejs/vite/issues/1807
- Vite.AspNetCore #157 — related middleware-mode WS routing issue:
  https://github.com/Eptagone/Vite.AspNetCore/issues/157
- Microsoft Learn — WebSockets in ASP.NET Core (HTTP/2 WebSockets section):
  https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets
- RFC 8441 — Bootstrapping WebSockets with HTTP/2:
  https://datatracker.ietf.org/doc/html/rfc8441
