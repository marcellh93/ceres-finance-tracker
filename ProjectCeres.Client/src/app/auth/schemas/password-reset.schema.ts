import { z } from 'zod';

export const passwordResetRequestSchema = z.object({
  email: z.string().email('Enter a valid email address.'),
});
export type PasswordResetRequestFormValues = z.infer<typeof passwordResetRequestSchema>;

// Confirm form: new password + match. The mismatch refinement attaches to the
// confirm field so the error renders where the user last typed.
export const passwordResetConfirmSchema = z
  .object({
    newPassword: z.string().min(8, 'Use at least 8 characters.'),
    confirmPassword: z.string(),
    totpCode: z.string().optional(),
  })
  .refine((v) => v.newPassword === v.confirmPassword, {
    message: 'Passwords do not match.',
    path: ['confirmPassword'],
  });
export type PasswordResetConfirmFormValues = z.infer<typeof passwordResetConfirmSchema>;
