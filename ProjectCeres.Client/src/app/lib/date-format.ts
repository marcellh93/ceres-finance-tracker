export type DateFormat = 'DD/MM/YYYY' | 'MM/DD/YYYY' | 'YYYY-MM-DD';

/**
 * Formats an ISO yyyy-MM-dd date according to the user's configured date
 * format. Unknown formats fall back to DD/MM/YYYY (the application default
 * set by SettingsService).
 */
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

export function formatDate(yyyyMmDd: string, format: string | undefined): string {
  const match = ISO_DATE.exec(yyyyMmDd);
  if (!match) return yyyyMmDd;
  const [, y, m, d] = match;

  switch (format) {
    case 'MM/DD/YYYY': return `${m}/${d}/${y}`;
    case 'YYYY-MM-DD': return `${y}-${m}-${d}`;
    case 'DD/MM/YYYY':
    default:           return `${d}/${m}/${y}`;
  }
}
