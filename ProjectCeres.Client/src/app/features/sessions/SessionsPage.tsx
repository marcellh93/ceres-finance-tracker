import { useCallback, useEffect, useState } from 'react';
import { Ban, Shield, ShieldCheck } from 'lucide-react';
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
import { useStepUp, ReauthCancelledError, ReauthBusyError } from '../../auth/use-step-up';
import { useAuth } from '../../auth/auth-context';
import {
  anchorUrl,
  BLOCK_IP_URL,
  BLOCKED_IPS_URL,
  SESSIONS_URL,
  sessionUrl,
  type BlockedIpDto,
  type SessionDto,
} from './sessions-api';
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
  const [blockedIps, setBlockedIps] = useState<BlockedIpDto[]>([]);
  const [pendingUnblock, setPendingUnblock] = useState<BlockedIpDto | null>(null);
  const [unblockingIp, setUnblockingIp] = useState<string | null>(null);
  const [pendingAnchor, setPendingAnchor] = useState<SessionDto | null>(null);
  const [anchoringId, setAnchoringId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    try {
      // Both reads are reauth-gated; wrapping the pair in one requireStepUp means a
      // single identity check covers the whole page load, not one prompt per request.
      const result = await requireStepUp(async () => {
        const sessionsResult = await apiFetch<SessionDto[]>(SESSIONS_URL);
        const blockedResult = await apiFetch<BlockedIpDto[]>(BLOCKED_IPS_URL);
        return { sessionsResult, blockedResult };
      });
      if (result.sessionsResult.ok) {
        setSessions(result.sessionsResult.data ?? []);
        // The blocked list is secondary; if only it fails, keep the page usable and
        // leave the section empty rather than blocking the whole load on it.
        setBlockedIps(result.blockedResult.ok ? (result.blockedResult.data ?? []) : []);
      } else {
        setError(new Error(result.sessionsResult.message));
      }
    } catch (err) {
      // Cancelling the reauth dialog is a deliberate choice, not a failure —
      // show the error state without a toast scolding the user.
      if (err instanceof ReauthCancelledError) {
        setError(new Error('Confirm your identity to view active sessions.'));
      } else if (err instanceof ReauthBusyError) {
        // Written copy, not the raw exception message. Busy is not a user choice,
        // so it must say something — but "A reauthentication prompt is already
        // open." is an internal string, and every sibling path here uses a sentence
        // written for the reader. Found by the 12.8 review, which also caught that
        // this file holds three of the eight requireStepUp call sites — the
        // "four call sites" framing had left it unaudited.
        setError(new Error('Finish the identity check already open, then try again.'));
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

  async function confirmUnblock() {
    const target = pendingUnblock;
    if (target === null) return;
    setPendingUnblock(null);
    setUnblockingIp(target.ipAddress);

    try {
      const result = await requireStepUp(() =>
        apiFetch(BLOCKED_IPS_URL, { method: 'DELETE', body: { ipAddress: target.ipAddress } }),
      );

      if (!result.ok) {
        toast.error(result.message || 'Could not unblock that address.');
        return;
      }

      toast.success(`Unblocked ${target.ipAddress}.`);
      await load();
    } catch (err) {
      if (!(err instanceof ReauthCancelledError)) {
        toast.error('Could not unblock that address.');
      }
    } finally {
      setUnblockingIp(null);
    }
  }

  async function confirmAnchor() {
    const target = pendingAnchor;
    if (target === null) return;
    const next = !target.isIpAnchored;
    setPendingAnchor(null);
    setAnchoringId(target.id);

    try {
      const result = await requireStepUp(() =>
        apiFetch(anchorUrl(target.id), { method: 'POST', body: { anchored: next } }),
      );

      if (!result.ok) {
        toast.error(result.message || 'Could not update this session.');
        return;
      }

      toast.success(next ? 'Session anchored to its IP.' : 'IP anchor removed.');
      await load();
    } catch (err) {
      if (!(err instanceof ReauthCancelledError)) {
        toast.error('Could not update this session.');
      }
    } finally {
      setAnchoringId(null);
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
                    className="flex flex-col gap-3 py-4 lg:flex-row lg:items-center lg:justify-between"
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
                      <div className="flex flex-wrap items-center gap-2">
                        {session.isIpAnchored && (
                          <span className="text-primary inline-flex items-center gap-1 text-xs font-medium">
                            <ShieldCheck aria-hidden="true" className="size-3.5" />
                            Anchored to IP
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
                    <div className="flex shrink-0 flex-wrap gap-2">
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={anchoringId === session.id}
                        onClick={() => setPendingAnchor(session)}
                      >
                        {session.isIpAnchored ? (
                          <>
                            <ShieldCheck aria-hidden="true" />
                            Anchored
                          </>
                        ) : (
                          <>
                            <Shield aria-hidden="true" />
                            Anchor IP
                          </>
                        )}
                      </Button>
                      {/* No block action on the current row: blocking the address
                          you are connected from locks you out, and there is no
                          unblock path. The server refuses it too (409). */}
                      {!session.isCurrent && (
                        <Button
                          variant="destructive"
                          size="sm"
                          disabled={blockingIp === session.ipCreatedAt}
                          onClick={() => setPendingBlock(session)}
                        >
                          <Ban aria-hidden="true" />
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

      {/* Blocked addresses. Rendered only when the user has blocks — an empty
          section is noise. A block you cannot see is a block you cannot reverse. */}
      {blockedIps.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle>Blocked addresses</CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="divide-border divide-y">
              {blockedIps.map((blocked) => (
                <li
                  key={blocked.ipAddress}
                  className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between"
                >
                  <div className="min-w-0 space-y-1">
                    <span className="font-medium">{blocked.ipAddress}</span>
                    <p className="text-muted-foreground text-sm">
                      Blocked {new Date(blocked.blockedAt).toLocaleString()}
                    </p>
                  </div>
                  <div className="flex shrink-0 gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={unblockingIp === blocked.ipAddress}
                      onClick={() => setPendingUnblock(blocked)}
                    >
                      Unblock
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}

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
            <AlertDialogAction variant="destructive" onClick={() => void confirmRevoke()}>
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
              refused. You can lift the block later under Blocked addresses below.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={() => void confirmBlock()}>
              Block address
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog
        open={pendingUnblock !== null}
        onOpenChange={(open) => !open && setPendingUnblock(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Unblock {pendingUnblock?.ipAddress}?</AlertDialogTitle>
            <AlertDialogDescription>
              Sign-ins from this address will be allowed again. Sessions that were signed out
              when you blocked it stay signed out — this only restores future access.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={() => void confirmUnblock()}>Unblock</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog
        open={pendingAnchor !== null}
        onOpenChange={(open) => !open && setPendingAnchor(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {pendingAnchor?.isIpAnchored
                ? 'Remove the IP anchor?'
                : `Anchor this session to ${pendingAnchor?.ipCreatedAt}?`}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {pendingAnchor?.isIpAnchored
                ? 'This session will work from any network again. A stolen session cookie replayed from another address would no longer be rejected.'
                : pendingAnchor?.isCurrent
                  ? `This session will stop working the moment your IP changes — and you will be signed out on this device and have to sign in again. Only anchor if this network is stable (e.g. an office desktop). It protects against a stolen session cookie replayed from another network; it does not block someone who signs in with your password.`
                  : `This session will stop working the moment its IP changes, and it will be signed out. It protects against a stolen session cookie replayed from another network; it does not block someone who signs in with your password.`}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={() => void confirmAnchor()}>
              {pendingAnchor?.isIpAnchored ? 'Remove anchor' : 'Anchor session'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
