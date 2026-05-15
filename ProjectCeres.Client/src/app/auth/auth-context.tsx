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

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return (
    <AuthContext.Provider value={{ status, user, refresh }}>
      {children}
    </AuthContext.Provider>
  );
}
