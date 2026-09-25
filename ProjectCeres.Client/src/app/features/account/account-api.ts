import { apiFetch } from '../../lib/api-client';

export const EXPORT_URL = '/api/profile/export';

export interface ExportJobAccepted {
  jobId: string;
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
