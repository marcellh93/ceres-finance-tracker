/**
 * Stage 9.6 — Backup-codes .txt synthesis + download helpers.
 *
 * Pulled into its own module so it can be unit-tested in jsdom without
 * mounting the whole wizard. The wizard step component imports
 * `downloadBackupCodes` and the txt-synthesis helper.
 */

export function synthesizeBackupCodesTxt(codes: readonly string[], email: string): string {
  const generatedAt = new Date().toISOString().slice(0, 10); // YYYY-MM-DD
  const header = [
    'Ceres — two-factor sign-in backup codes',
    `Account: ${email}`,
    `Generated: ${generatedAt}`,
    '',
    'Each code works once. Use a code from this list if you lose access to',
    'your authenticator app. Keep this file somewhere safe (password manager,',
    'printed copy in a drawer, encrypted note).',
    '',
  ].join('\n');
  const body = codes.join('\n');
  return `${header}${body}\n`;
}

/**
 * Synthesises a .txt Blob and programmatically clicks an <a download> element
 * so the browser saves the file. Revokes the object URL on the next tick.
 * Pure side effect; no return value. Intentionally narrow surface so the
 * test can mock URL.createObjectURL + click.
 */
export function downloadBackupCodes(codes: readonly string[], email: string, filename: string): void {
  const text = synthesizeBackupCodesTxt(codes, email);
  const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  // Defer revoke so the browser has time to start the download.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
