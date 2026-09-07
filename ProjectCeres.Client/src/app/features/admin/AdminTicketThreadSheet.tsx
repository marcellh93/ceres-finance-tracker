import { useCallback, useEffect, useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Textarea } from '@/components/ui/textarea';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { apiFetch } from '../../lib/api-client';
import {
  SupportMessageAuthor,
  SupportTicketStatus,
  statusBadgeVariant,
  statusLabel,
} from '../support/support-api';
import { getAdminTicket, adminTicketMessagesUrl, type AdminTicketThreadDto } from './admin-support-api';

type Props = {
  ticketId: string;
  onClose: () => void;
  onChanged: () => void;
};

/**
 * Stage 12.5.2 — operator thread view in a right-side sheet at /admin/support/:id.
 * Reads the admin thread; posts a reply and/or a status change through the EXISTING
 * operator endpoint (POST /api/admin/support/tickets/{id}/messages, Stage 12.6). Open
 * state is derived from the route — closing navigates, it does not toggle local state.
 */
export function AdminTicketThreadSheet({ ticketId, onClose, onChanged }: Props) {
  const [thread, setThread] = useState<AdminTicketThreadDto | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [reply, setReply] = useState('');
  const [nextStatus, setNextStatus] = useState<SupportTicketStatus | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getAdminTicket(ticketId);
      if (result.ok) {
        setThread(result.data ?? undefined);
        setNextStatus(result.data?.status ?? null);
      }
    } finally {
      setLoading(false);
    }
  }, [ticketId]);

  useEffect(() => {
    void load();
  }, [load]);

  async function submit() {
    if (thread === undefined || nextStatus === null) return;
    setSubmitting(true);
    try {
      const result = await apiFetch(adminTicketMessagesUrl(ticketId), {
        method: 'POST',
        body: { body: reply.trim() || null, status: nextStatus },
      });
      if (!result.ok) {
        toast.error(result.message || 'Could not update the ticket.');
        return;
      }
      toast.success('Ticket updated.');
      setReply('');
      await load();
      onChanged();
    } catch {
      toast.error('Could not update the ticket.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Sheet open onOpenChange={(o) => !o && onClose()}>
      <SheetContent className="flex w-full flex-col sm:max-w-lg">
        <SheetHeader>
          <SheetTitle className="flex items-center gap-2">
            <span className="truncate">{thread?.subject ?? 'Ticket'}</span>
            {thread && (
              <Badge variant={statusBadgeVariant[thread.status]} className="shrink-0">
                {statusLabel[thread.status]}
              </Badge>
            )}
          </SheetTitle>
          <SheetDescription>{thread ? `Filed by ${thread.ownerEmail}` : 'Loading…'}</SheetDescription>
        </SheetHeader>

        <div className="flex-1 space-y-4 overflow-y-auto py-4">
          {loading && !thread ? (
            <><Skeleton className="h-16 w-full" /><Skeleton className="h-16 w-full" /></>
          ) : (
            thread?.messages.map((m) => (
              <div
                key={m.id}
                className={
                  m.authorRole === SupportMessageAuthor.Agent
                    ? 'bg-primary/5 rounded-md border border-border p-3'
                    : 'rounded-md border border-border p-3'
                }
              >
                <p className="text-muted-foreground mb-1 text-xs font-medium">
                  {m.authorRole === SupportMessageAuthor.Agent ? 'Operator' : 'User'} ·{' '}
                  {new Date(m.createdAt).toLocaleString()}
                </p>
                <p className="whitespace-pre-wrap text-sm">{m.body}</p>
              </div>
            ))
          )}
        </div>

        <div className="space-y-3 border-t border-border pt-4">
          <Textarea
            placeholder="Write an operator reply (optional — you can change status without a reply)"
            value={reply}
            onChange={(e) => setReply(e.target.value)}
            rows={3}
          />
          <div className="flex flex-wrap items-center gap-3">
            <select
              aria-label="Set status"
              className="h-9 rounded-md border border-input bg-background px-3 text-sm"
              value={nextStatus === null ? '' : String(nextStatus)}
              onChange={(e) => setNextStatus(Number(e.target.value) as SupportTicketStatus)}
            >
              {Object.values(SupportTicketStatus).map((s) => (
                <option key={s} value={String(s)}>{statusLabel[s]}</option>
              ))}
            </select>
            <Button type="button" disabled={submitting || nextStatus === null} onClick={() => void submit()}>
              {submitting ? 'Saving…' : 'Update ticket'}
            </Button>
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
}
