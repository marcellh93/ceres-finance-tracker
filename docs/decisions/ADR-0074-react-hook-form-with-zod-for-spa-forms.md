# ADR-0074 — react-hook-form + zod for SPA forms

## Status: Accepted (2026-05-16, Stage 9 Phase 1)

## Context

Project Ceres' SPA had no form library before Stage 9. Forms were built with plain `useState` per field, ad-hoc validators, and the `<Field>` wrapper at `src/app/components/Field.tsx` for layout. That pattern works for simple forms (a single text input + submit button) but pushes its limits when:

- Multiple fields need cross-field validation (e.g. `password` matches `confirmPassword`).
- Submission needs an in-flight loading state plus a success/failure outcome.
- Field-level errors must move keyboard focus to the first errored field on submit (an a11y requirement called out in `roadmap-phase-three.md:1049`).
- Multi-step forms need state spanning steps (`/security/totp/setup` is enroll → verify → backup-codes-acknowledged).
- Conditional fields appear based on other field values (TOTP code on `/password-reset/confirm` only when the user has TOTP enabled).

Stage 9 ships **nine forms** with these characteristics. Hand-rolling state + validation + focus management + submission per page would mean 80–120 lines of plumbing per form, repeated across nine pages, with no enforcement of consistency.

## Decision

Adopt **`react-hook-form`** for form state, validation, error tracking, focus management, and submission lifecycle. Pair with **`zod`** + **`@hookform/resolvers`** for schema-driven validation that doubles as TypeScript types via `z.infer<typeof schema>`.

Bundle size impact: ~24kB gzipped combined.

## Pattern

Each form has:
- A zod schema in `src/app/auth/schemas/<feature>.schema.ts` (or per-feature folder).
- A page component that calls `useForm({ resolver: zodResolver(schema) })`.
- Inputs registered with `{...register('field')}`.
- Submit handler wrapped in `form.handleSubmit(onSubmit)` so the form is a real `<form onSubmit={...}>` (Enter submits per the project's Rule 4 in `2026-05-15-stage-9-auth-spa-pages-design.md` § 4).
- `<Button type="submit">` (the shadcn `<Button>` defaults to `type="button"`, so the explicit `type="submit"` is required).

## Migration policy

Existing non-auth forms (Movements, Categories, Budgets, Accounts, Recurring, Settings, Profile, Security, Import) **stay on the current `useState + <Field>` pattern**. They are not in the path of Stage 9, and a big-bang migration would introduce churn without immediate value. When any of those pages is touched for unrelated work, the implementer may choose to migrate it as part of that work — but is not required to.

The new pattern applies to:
- Every new form built from Stage 9 onwards.
- Any auth-page form (the entire `src/app/pages/auth/` tree).

## Alternatives considered

- **Plain `useState` + grow the `<Field>` wrapper** — rejected. The four genuinely-complex Stage 9 forms (Register with strength meter + cross-field, TOTP setup multi-step, Password-reset confirm with conditional TOTP, the reauth dialog with conditional input shape) push hand-rolled state past where it pays for itself.
- **Add only `zod` (validation), keep `useState` (state)** — rejected. ~15kB saved but the focus-on-first-error hook would still be hand-rolled, which is the part most likely to drift across pages.

## Consequences

- New top-level deps: `react-hook-form`, `zod`, `@hookform/resolvers`. Plus `qrcode.react` (for TOTP setup) and the shadcn `input-otp` component (for the 6-cell OTP input). All installed via pnpm per `feedback_pnpm_only_never_npm`.
- Project-wide form pattern documented in this ADR; future contributors know which pattern is canonical for new work.
- No migration burden on existing forms; they migrate opportunistically when touched for other reasons.
