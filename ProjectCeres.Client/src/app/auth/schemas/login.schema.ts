import { z } from 'zod';

// Schema messages are i18n KEYS, not English strings. Schemas evaluate at
// module-init time (before i18n is ready), so resolution happens in the form
// component via `t(errors.<field>?.message ?? '')`. See
// docs/superpowers/specs/... or the Login.tsx render site for the consumption
// pattern. Bug origin: 2026-05-18 — zod errors rendered in English even when
// the page was Spanish.
export const loginSchema = z.object({
  email: z.string().email('auth.validation.email'),
  password: z.string().min(1, 'auth.validation.passwordRequired'),
  rememberMe: z.boolean(),
});

export type LoginFormValues = z.infer<typeof loginSchema>;
