import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { apiFetch } from '../lib/api-client';

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
    setUser(null);
    setStatus('anon');
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return (
    <AuthContext.Provider value={{ status, user, refresh, logout }}>
      {children}
    </AuthContext.Provider>
  );
}
