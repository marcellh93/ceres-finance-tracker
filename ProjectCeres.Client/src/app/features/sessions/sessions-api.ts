// ---------- URL builders ----------

export const SESSIONS_URL = '/api/sessions';
export const sessionUrl = (id: string) => `/api/sessions/${id}`;
export const BLOCK_IP_URL = '/api/sessions/block-ip';

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
};
