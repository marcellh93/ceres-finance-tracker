// ---------- URL builders ----------

export const SESSIONS_URL = '/api/sessions';
export const sessionUrl = (id: string) => `/api/sessions/${id}`;
export const anchorUrl = (id: string) => `/api/sessions/${id}/anchor`;
export const BLOCK_IP_URL = '/api/sessions/block-ip';
export const BLOCKED_IPS_URL = '/api/sessions/blocked-ips';

// ---------- DTOs ----------

/**
 * Mirrors `ProjectCeres/ViewModels/Sessions/SessionDto.cs`.
 *
 * `ipCreatedAt` is the IP the session was created from, not a timestamp —
 * the name reads like one. `userAgent` is the raw header (up to 512 chars);
 * render it through `summarizeUserAgent`, never directly.
 */
export type SessionDto = {
  id: string;
  createdAt: string;
  lastUsedAt: string;
  ipCreatedAt: string;
  userAgent: string;
  isCurrent: boolean;
  /**
   * When true, this session only works from `ipCreatedAt`. A request from any
   * other IP signs it out. Opt-in per session; defends a stolen cookie replayed
   * from another network.
   */
  isIpAnchored: boolean;
};

/** Mirrors `ProjectCeres/ViewModels/Sessions/BlockedIpDto.cs`. */
export type BlockedIpDto = {
  ipAddress: string;
  blockedAt: string;
};
