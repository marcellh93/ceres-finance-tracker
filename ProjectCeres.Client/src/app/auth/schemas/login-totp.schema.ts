import { z } from 'zod';

export const totpCodeSchema = z.object({
  code: z.string().regex(/^\d{6}$/, 'Enter the 6-digit code.'),
});
export type TotpCodeFormValues = z.infer<typeof totpCodeSchema>;

export const backupCodeSchema = z.object({
  code: z.string().min(1, 'Enter a backup code.').max(32),
});
export type BackupCodeFormValues = z.infer<typeof backupCodeSchema>;
