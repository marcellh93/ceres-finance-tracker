import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from './auth-context';

/**
 * Guards protected routes. Reads the auth context:
 * - 'loading' → renders null (brief, no skeleton needed; /api/auth/me is fast)
 * - 'anon'    → redirects to /login?redirect=<currentPath>
 * - 'authed'  → renders children
 *
 * Must be rendered inside <AuthProvider>. useAuth() throws loudly at dev time
 * if the provider is missing so misuse surfaces immediately.
 */
export function RequireAuth({ children }: { children: NonNullable<ReactNode> }) {
  const auth = useAuth();
  const location = useLocation();

  if (auth.status === 'loading') {
    return null;
  }

  if (auth.status === 'anon') {
    return <Navigate to={`/login?redirect=${encodeURIComponent(location.pathname)}`} replace />;
  }

  return <>{children}</>;
}
