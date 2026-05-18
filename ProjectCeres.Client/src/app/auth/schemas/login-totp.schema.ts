import { z } from 'zod';

// Messages are i18n keys — resolve via `t(...)` at render time.
// See login.schema.ts for the rationale.
export const totpCodeSchema = z.object({
  code: z.string().regex(/^\d{6}$/, 'auth.validation.totpCodeShape'),
});
export type TotpCodeFormValues = z.infer<typeof totpCodeSchema>;

export const backupCodeSchema = z.object({
  code: z.string().min(1, 'auth.validation.backupCodeRequired').max(32),
});
export type BackupCodeFormValues = z.infer<typeof backupCodeSchema>;
