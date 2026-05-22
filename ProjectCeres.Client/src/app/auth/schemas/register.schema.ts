import { z } from 'zod';

// Messages are i18n keys — resolve via `t(...)` at render time. See
// login.schema.ts / password-reset.schema.ts for the rationale.
export const registerSchema = z.object({
  email: z.string().email('auth.register.errors.invalidEmail'),
  password: z.string().min(8, 'auth.register.errors.passwordTooShort'),
});

export type RegisterFormValues = z.infer<typeof registerSchema>;
