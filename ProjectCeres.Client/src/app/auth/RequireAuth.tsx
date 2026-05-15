import type { ReactNode } from 'react';

/**
 * Phase 1 stub: always allows the children through. The real auth-state
 * gate arrives in Task 4 once <AuthProvider> exists. Splitting the layout
 * commit (this one) from the auth-context commit (Task 4) keeps each
 * commit reviewable and testable in isolation.
 *
 * Real behaviour after Task 4: reads useAuth(); if status === 'anon',
 * navigates to /login?redirect=<currentPath>; otherwise renders children.
 */
export function RequireAuth({ children }: { children: NonNullable<ReactNode> }) {
  return <>{children}</>;
}
