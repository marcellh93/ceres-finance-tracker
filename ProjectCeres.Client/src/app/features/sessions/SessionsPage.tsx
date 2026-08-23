import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { useDocumentTitle } from '../../lib/use-document-title';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { apiFetch } from '../../lib/api-client';
import { useStepUp, ReauthCancelledError } from '../../auth/use-step-up';
import { useAuth } from '../../auth/auth-context';
import { BLOCK_IP_URL, SESSIONS_URL, sessionUrl, type SessionDto } from './sessions-api';
import { summarizeUserAgent } from './user-agent-summary';

/**
 * Active-session list with per-row revoke.
 *
 * Fetches through `apiFetch` wrapped in `requireStepUp`, NOT the usual
 * `useApi` hook: these endpoints answer `401 REAUTH_REQUIRED`, and `useApi`
 * treats every 401 as a sign-out — it would log the user out on arrival.
 * `apiFetch` distinguishes the two, and `requireStepUp` opens the reauth
 * dialog and replays the request.
 *
 * The reauth window is 5 minutes and does not roll forward on use, so a
 * revoke clicked after reading the list can 401 on its own. Every request
 * here is wrapped, not just the initial load.
 */
export function SessionsPage() {
  useDocumentTitle('Active sessions');
  const { requireStepUp } = useStepUp();
  const { logout } = useAuth();
  const navigate = useNavigate();

  const [sessions, setSessions] = useState<SessionDto[] | undefined>(undefined);
  const [error, setError] = useState<Error | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [pendingRevoke, setPendingRevoke] = useState<SessionDto | null>(null);
  const [revokingId, setRevokingId] = useState<string | null>(null);
  const [pendingBlock, setPendingBlock] = useState<SessionDto | null>(null);
  const [blockingIp, setBlockingIp] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    try {
      const result = await requireStepUp(() => apiFetch<SessionDto[]>(SESSIONS_URL));
      if (result.ok) {
        setSessions(result.data ?? []);
      } else {
        setError(new Error(result.message));
      }
    } catch (err) {
      // Cancelling the reauth dialog is a deliberate choice, not a failure —
      // show the error state without a toast scolding the user.
      if (err instanceof ReauthCancelledError) {
        setError(new Error('Confirm your identity to view active sessions.'));
      } else {
        setError(err instanceof Error ? err : new Error(String(err)));
      }
    } finally {
      setLoading(false);
    }
  }, [requireStepUp]);

  useEffect(() => {
    void load();
  }, [load]);

  async function confirmRevoke() {
    const target = pendingRevoke;
    if (target === null) return;
    setPendingRevoke(null);
    setRevokingId(target.id);

    try {
      const result = await requireStepUp(() =>
        apiFetch(sessionUrl(target.id), { method: 'DELETE' }),
      );

      if (!result.ok) {
        toast.error(result.message || 'Could not revoke that session.');
        return;
      }

      if (target.isCurrent) {
        // Revoking your own session ends it server-side; the next request
        // would 401. Clear local auth state and leave deliberately rather
        // than letting the app discover it mid-render.
        toast.success('Signed out on this device.');
        await logout();
        void navigate('/login');
        return;
      }

      toast.success('Session revoked.');
      // Refetch rather than splice: after a mid-action reauth the list on
      // screen may be minutes stale, and other sessions may have changed.
      await load();
    } catch (err) {
      if (!(err instanceof ReauthCancelledError)) {
        toast.error('Could not revoke that session.');
      }
    } finally {
      setRevokingId(null);
    }
  }

  async function confirmBlock() {
    const target = pendingBlock;
    if (target === null) return;
    setPendingBlock(null);
    setBlockingIp(target.ipCreatedAt);

    try {
      const result = await requireStepUp(() =>
        apiFetch(BLOCK_IP_URL, { method: 'POST', body: { ipAddress: target.ipCreatedAt } }),
      );

      if (!result.ok) {
        // 409 SELF_LOCKOUT: the server refuses to block the address the caller
        // is connected from. The UI hides the action on the current row, so
        // reaching this means the address is shared with the current session.
        toast.error(result.message || 'Could not block that address.');
        return;
      }

      toast.success(`Blocked ${target.ipCreatedAt}.`);
      await load();
    } catch (err) {
      if (!(err instanceof ReauthCancelledError)) {
        toast.error('Could not block that address.');
      }
    } finally {
      setBlockingIp(null);
    }
  }

  const hasData = sessions !== undefined;
  const showSkeleton = useDelayedLoading(loading && !hasData);

  let state: DataTransitionState;
  if (showSkeleton && !hasData) state = 'skeleton';
  else if (error && !hasData) state = 'error';
  else state = 'data';

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Active sessions</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          Every device currently signed in to your account. Revoke any you don&apos;t recognise.
        </p>
      </div>

      <DataTransition
        state={state}
        skeleton={
          <Card>
            <CardContent className="space-y-3 pt-6">
              <Skeleton className="h-16 w-full" />
              <Skeleton className="h-16 w-full" />
            </CardContent>
          </Card>
        }
        error={
          <Card>
            <CardContent className="space-y-4 pt-6">
              <p className="text-muted-foreground text-sm">
                {error?.message ?? 'Could not load your sessions.'}
              </p>
              <Button variant="outline" onClick={() => void load()}>
                Try again
              </Button>
            </CardContent>
          </Card>
        }
      >
        <Card>
          <CardHeader>
            <CardTitle>
              {/* `?? 0` here would announce "0 active sessions" while the list
                  is still loading, contradicting the body directly below it. */}
              {sessions === undefined
                ? 'Active sessions'
                : sessions.length === 1
                  ? '1 active session'
                  : `${sessions.length} active sessions`}
            </CardTitle>
          </CardHeader>
          <CardContent>
            {sessions !== undefined && sessions.length === 0 ? (
              <p className="text-muted-foreground py-6 text-center text-sm">
                No other active sessions.
              </p>
            ) : (
              <ul className="divide-border divide-y">
                {sessions?.map((session) => (
                  <li
                    key={session.id}
                    className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between"
                  >
                    <div className="min-w-0 space-y-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-medium">{summarizeUserAgent(session.userAgent)}</span>
                        {session.isCurrent && (
                          <span className="bg-primary/10 text-primary rounded-full px-2 py-0.5 text-xs font-medium">
                            This device
                          </span>
                        )}
                      </div>
                      <p className="text-muted-foreground text-sm">
                        IP {session.ipCreatedAt} · Last used{' '}
                        {new Date(session.lastUsedAt).toLocaleString()}
                      </p>
                      <p className="text-muted-foreground text-xs">
                        Signed in {new Date(session.createdAt).toLocaleString()}
                      </p>
                    </div>
                    <div className="flex shrink-0 gap-2">
                      {/* No block action on the current row: blocking the address
                          you are connected from locks you out, and there is no
                          unblock path. The server refuses it too (409). */}
                      {!session.isCurrent && (
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={blockingIp === session.ipCreatedAt}
                          onClick={() => setPendingBlock(session)}
                        >
                          Block IP
                        </Button>
                      )}
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={revokingId === session.id}
                        onClick={() => setPendingRevoke(session)}
                      >
                        {session.isCurrent ? 'Sign out' : 'Revoke'}
                      </Button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      </DataTransition>

      <AlertDialog
        open={pendingRevoke !== null}
        onOpenChange={(open) => !open && setPendingRevoke(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {pendingRevoke?.isCurrent ? 'Sign out on this device?' : 'Revoke this session?'}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {pendingRevoke?.isCurrent
                ? 'You will be signed out and returned to the login page.'
                : `${summarizeUserAgent(pendingRevoke?.userAgent)} will be signed out immediately. This cannot be undone.`}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={() => void confirmRevoke()}>
              {pendingRevoke?.isCurrent ? 'Sign out' : 'Revoke'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog
        open={pendingBlock !== null}
        onOpenChange={(open) => !open && setPendingBlock(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Block {pendingBlock?.ipCreatedAt}?</AlertDialogTitle>
            <AlertDialogDescription>
              Every session from this address is signed out, and future sign-ins from it are
              refused. You cannot undo this from the app yet, so only block an address you are
              sure you will not need.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={() => void confirmBlock()}>Block address</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
