import type { ReactNode } from 'react';

/**
 * Stage 12.5.2, fork 2a. Admin route gate that deliberately holds NO cached "is admin"
 * flag — there is no `isAdmin` on the session/`AuthUser`, and there must not be one:
 * ADR-0080 makes admin a LIVE per-request server check so a revoked admin loses access
 * immediately, and a cached client boolean would go stale until the next reload.
 *
 * So this gate is UX-only: it renders its children (an admin page). The page's own API
 * call to an `[RequireAdmin]` endpoint is the authority — a non-admin gets a 403 and the
 * page renders its "not authorized" state. The server, not this component, decides access.
 *
 * It exists as a named boundary so the route tree reads clearly and a future cheap
 * client-side pre-check (if ever wanted) has one place to live.
 */
export function RequireAdmin({ children }: { children: NonNullable<ReactNode> }) {
  return <>{children}</>;
}
