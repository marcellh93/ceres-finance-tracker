import { apiFetch } from '../../lib/api-client';

export const EXPORT_URL = '/api/profile/export';
export const ERASURE_URL = '/api/profile/erasure';

export interface ExportJobAccepted {
  jobId: string;
  message: string;
}

export interface ErasureAccepted {
  message: string;
}

/**
 * Requests a GDPR data export. Server returns 202 with the job id; the ZIP is
 * built by a background worker and a download link is emailed when ready.
 * Reauth-gated on the server ([RequireRecentAuth]) and rate-limited 1/24h (429).
 */
export function requestDataExport() {
  return apiFetch<ExportJobAccepted>(EXPORT_URL, { method: 'POST' });
}

/**
 * Requests account erasure. Seals the account immediately (202) and emails a
 * 72h cancel link; a background worker executes the erasure once that window
 * lapses. Reauth-gated ([RequireRecentAuth]), rate-limited 1/24h (429), and
 * requires the exact case-sensitive body { confirm: "ERASE" } (422 otherwise).
 */
export function requestErasure() {
  return apiFetch<ErasureAccepted>(ERASURE_URL, { method: 'POST', body: { confirm: 'ERASE' } });
}
