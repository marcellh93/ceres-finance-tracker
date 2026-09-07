import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { useDocumentTitle } from '../../lib/use-document-title';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import {
  SupportTicketStatus,
  SupportTicketPriority,
  statusBadgeVariant,
  statusLabel,
  priorityLabel,
} from '../support/support-api';
import { AdminTicketThreadSheet } from './AdminTicketThreadSheet';
import { listAdminTickets, type AdminTicketListResponse } from './admin-support-api';

const PAGE_SIZE = 25;

/**
 * Stage 12.5.2 — the operator triage list: every user's support tickets, paginated, with
 * status/priority filters. Fetches via apiFetch (not useApi) so a 403 renders a "not
 * authorized" state rather than being treated as a sign-out — the server [RequireAdmin]
 * check is the authority (fork 2a: no cached admin flag). Row → thread sheet at
 * /admin/support/:ticketId.
 */
export function AdminTicketListPage() {
  useDocumentTitle('Support (admin)');
  const navigate = useNavigate();
  const { ticketId } = useParams<{ ticketId: string }>();

  const [resp, setResp] = useState<AdminTicketListResponse | undefined>(undefined);
  const [error, setError] = useState<Error | undefined>(undefined);
  const [forbidden, setForbidden] = useState(false);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<SupportTicketStatus | 'all'>('all');
  const [priority, setPriority] = useState<SupportTicketPriority | 'all'>('all');

  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    setForbidden(false);
    try {
      const result = await listAdminTickets({
        page,
        pageSize: PAGE_SIZE,
        status: status === 'all' ? undefined : status,
        priority: priority === 'all' ? undefined : priority,
      });
      if (result.ok) {
        setResp(result.data ?? undefined);
      } else if (result.status === 403) {
        setForbidden(true);
      } else {
        setError(new Error(result.message));
      }
    } catch (err) {
      setError(err instanceof Error ? err : new Error(String(err)));
    } finally {
      setLoading(false);
    }
  }, [page, status, priority]);

  useEffect(() => {
    void load();
  }, [load]);

  const hasData = resp !== undefined;
  const showSkeleton = useDelayedLoading(loading && !hasData);

  if (forbidden) {
    return (
      <Card>
        <CardContent className="py-10 text-center">
          <p className="text-sm font-medium">You don&apos;t have access to this page.</p>
          <p className="text-muted-foreground mt-1 text-sm">
            The support admin area is available to operators only.
          </p>
        </CardContent>
      </Card>
    );
  }

  let state: DataTransitionState;
  if (showSkeleton && !hasData) state = 'skeleton';
  else if (error && !hasData) state = 'error';
  else state = 'data';

  const totalPages = resp ? Math.max(1, Math.ceil(resp.total / resp.pageSize)) : 1;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Support (admin)</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          Every support ticket across all users. Open one to reply or change its status.
        </p>
      </div>

      {/* Native <select> for the two filters — a filter control, not a form field, and
          it avoids pulling the base-ui Select primitive into the bundle (the project's
          established dropdown idiom is the Popover+Command combobox; a plain select is the
          lighter, zero-bundle choice for a filter). Styled with the shared input classes. */}
      <div className="flex flex-wrap gap-3">
        <select
          aria-label="Filter by status"
          className="h-9 rounded-md border border-input bg-background px-3 text-sm"
          value={String(status)}
          onChange={(e) => {
            setPage(1);
            setStatus(e.target.value === 'all' ? 'all' : (Number(e.target.value) as SupportTicketStatus));
          }}
        >
          <option value="all">All statuses</option>
          {Object.values(SupportTicketStatus).map((s) => (
            <option key={s} value={String(s)}>{statusLabel[s]}</option>
          ))}
        </select>
        <select
          aria-label="Filter by priority"
          className="h-9 rounded-md border border-input bg-background px-3 text-sm"
          value={String(priority)}
          onChange={(e) => {
            setPage(1);
            setPriority(e.target.value === 'all' ? 'all' : (Number(e.target.value) as SupportTicketPriority));
          }}
        >
          <option value="all">All priorities</option>
          {Object.values(SupportTicketPriority).map((p) => (
            <option key={p} value={String(p)}>{priorityLabel[p]}</option>
          ))}
        </select>
      </div>

      <DataTransition
        state={state}
        skeleton={
          <Card><CardContent className="space-y-3 pt-6">
            <Skeleton className="h-12 w-full" /><Skeleton className="h-12 w-full" />
          </CardContent></Card>
        }
        error={
          <Card><CardContent className="space-y-4 pt-6">
            <p className="text-muted-foreground text-sm">{error?.message ?? 'Could not load tickets.'}</p>
            <Button variant="outline" onClick={() => void load()}>Try again</Button>
          </CardContent></Card>
        }
      >
        <Card>
          <CardHeader>
            <CardTitle>{resp ? `${resp.total} ticket${resp.total === 1 ? '' : 's'}` : 'Tickets'}</CardTitle>
          </CardHeader>
          <CardContent>
            {resp && resp.items.length === 0 ? (
              <p className="text-muted-foreground py-6 text-center text-sm">No tickets match these filters.</p>
            ) : (
              <ul className="divide-border divide-y">
                {resp?.items.map((t) => (
                  <li key={t.id}>
                    <button
                      type="button"
                      onClick={() => navigate(`/admin/support/${t.id}`)}
                      className="hover:bg-muted/50 flex w-full items-center justify-between gap-3 py-4 text-left"
                    >
                      <div className="min-w-0 space-y-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="truncate font-medium">{t.subject}</span>
                          <Badge variant={statusBadgeVariant[t.status]} className="shrink-0">
                            {statusLabel[t.status]}
                          </Badge>
                        </div>
                        <p className="text-muted-foreground text-sm">
                          {t.ownerEmail} · {priorityLabel[t.priority]} · {t.messageCount} message
                          {t.messageCount === 1 ? '' : 's'} · last activity{' '}
                          {new Date(t.lastMessageAt).toLocaleString()}
                        </p>
                      </div>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      </DataTransition>

      {resp && totalPages > 1 && (
        <div className="flex items-center justify-between">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Previous
          </Button>
          <span className="text-muted-foreground text-sm">Page {resp.page} of {totalPages}</span>
          <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
            Next
          </Button>
        </div>
      )}

      {ticketId && (
        <AdminTicketThreadSheet
          ticketId={ticketId}
          onClose={() => navigate('/admin/support')}
          onChanged={() => void load()}
        />
      )}
    </div>
  );
}
