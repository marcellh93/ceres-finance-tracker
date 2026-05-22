import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { apiFetch, setOnUnauthenticated } from '../lib/api-client';
import { setCachedXsrfRequestToken } from './csrf';

export type AuthUser = {
  userId: string;
  email: string;
  twoFactorEnabled: boolean;
  lastReauthAt: number | null;
  backupCodesRemaining: number;
  usedBackupCodeAtLastLogin: boolean;
};

export type AuthStatus = 'loading' | 'anon' | 'authed';

type AuthContextValue = {
  status: AuthStatus;
  user: AuthUser | null;
  refresh: () => Promise<void>;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading');
  const [user, setUser] = useState<AuthUser | null>(null);

  const refresh = useCallback(async () => {
    const result = await apiFetch<AuthUser>('/api/auth/me');
    if (result.ok && result.data) {
      setUser(result.data);
      setStatus('authed');
    } else {
      setUser(null);
      setStatus('anon');
    }
  }, []);

  const logout = useCallback(async () => {
    // Fire the server logout, but clear local state unconditionally — even on
    // 401/500 the user wants to be signed out client-side; RequireAuth will
    // redirect on next render. apiFetch swallows network errors into ApiResult,
    // so this can't throw.
    await apiFetch('/api/auth/logout', { method: 'POST' });
    // Server rotates the CSRF cookie+token pair on logout (AuthController.cs
    // calls _antiforgery.GetAndStoreTokens). Clear our cached request token so
    // the next state-changing call (e.g. POST /api/auth/login) triggers a
    // fresh handshake; otherwise we'd send the stale token against the new
    // cookie and the server rejects with 400.
    setCachedXsrfRequestToken(null);
    setUser(null);
    setStatus('anon');
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Subscribe to silently-expired-session signals from apiFetch. When any
  // non-REAUTH_REQUIRED 401 comes back from a non-auth-probe URL (dashboard
  // chart fetch, movements list, etc.), drop to 'anon' so RequireAuth
  // redirects on the next render. Separate effect from the refresh() mount —
  // independent dependency, independent lifecycle.
  useEffect(() => {
    setOnUnauthenticated(() => {
      setUser(null);
      setStatus('anon');
      setCachedXsrfRequestToken(null);
    });
    return () => setOnUnauthenticated(null);
  }, []);

  return (
    <AuthContext.Provider value={{ status, user, refresh, logout }}>
      {children}
    </AuthContext.Provider>
  );
}
