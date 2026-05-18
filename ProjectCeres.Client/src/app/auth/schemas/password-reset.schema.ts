import { z } from 'zod';

// Messages are i18n keys — resolve via `t(...)` at render time.
// See login.schema.ts for the rationale.
export const passwordResetRequestSchema = z.object({
  email: z.string().email('auth.validation.email'),
});
export type PasswordResetRequestFormValues = z.infer<typeof passwordResetRequestSchema>;

// Confirm form: new password + match. The mismatch refinement attaches to the
// confirm field so the error renders where the user last typed.
export const passwordResetConfirmSchema = z
  .object({
    newPassword: z.string().min(8, 'auth.validation.passwordMinLength'),
    confirmPassword: z.string(),
    totpCode: z.string().optional(),
  })
  .refine((v) => v.newPassword === v.confirmPassword, {
    message: 'auth.validation.passwordMismatch',
    path: ['confirmPassword'],
  });
export type PasswordResetConfirmFormValues = z.infer<typeof passwordResetConfirmSchema>;
