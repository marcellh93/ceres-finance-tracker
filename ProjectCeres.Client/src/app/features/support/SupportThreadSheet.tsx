import { useState } from 'react';
import { Paperclip } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
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
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { useApi } from '../../lib/use-api';
import { apiFetch } from '../../lib/api-client';
import { ReplyComposer } from './ReplyComposer';
import {
  SupportMessageAuthor,
  SupportTicketStatus,
  statusBadgeVariant,
  statusLabel,
  supportAttachmentDownloadUrl,
  supportCloseUrl,
  supportRepliesUrl,
  supportTicketUrl,
  type SupportTicketThreadDto,
} from './support-api';

type SupportThreadSheetProps = {
  ticketId: string;
  /** Close the sheet — the page navigates back to /support. */
  onClose: () => void;
  /** Refetch the list after a reply moves the ticket's status/count. */
  onChanged: () => void;
  /** Start a follow-up ticket that continues a Closed one. */
  onFollowUp: (ticket: SupportTicketThreadDto) => void;
};

/**
 * The conversation, in a right-side slide-in sheet reflected at /support/<id>.
 * Open state is derived from the route — closing navigates, it does not just
 * toggle local state — so the URL and the sheet never disagree.
 */
export function SupportThreadSheet({
  ticketId,
  onClose,
  onChanged,
  onFollowUp,
}: SupportThreadSheetProps) {
  const thread = useApi<SupportTicketThreadDto>(supportTicketUrl(ticketId));

  async function postReply(body: string): Promise<string | null> {
    const result = await apiFetch<{ id: string }>(supportRepliesUrl(ticketId), {
      method: 'POST',
      body: { body },
    });
    return result.ok && result.data ? result.data.id : null;
  }

  // Called by the composer AFTER the reply and any attachment uploads land, so
  // the refetched thread includes the new message and its files in one pass.
  function handleReplyComplete() {
    thread.refetch();
    onChanged();
  }

  async function closeTicket(): Promise<void> {
    const result = await apiFetch(supportCloseUrl(ticketId), { method: 'POST' });
    if (!result.ok) {
      toast.error(result.message || 'Could not close the ticket.');
      return;
    }
    toast.success('Ticket closed.');
    thread.refetch();
    onChanged();
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent
        className="data-[side=right]:w-full data-[side=right]:sm:max-w-lg"
        side="right"
      >
        {thread.loading ? (
          <ThreadSkeleton />
        ) : thread.error || !thread.data ? (
          <ThreadError onClose={onClose} />
        ) : (
          <ThreadBody
            data={thread.data}
            onReply={postReply}
            onReplyComplete={handleReplyComplete}
            onFollowUp={onFollowUp}
            onCloseTicket={closeTicket}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function ThreadBody({
  data,
  onReply,
  onReplyComplete,
  onFollowUp,
  onCloseTicket,
}: {
  data: SupportTicketThreadDto;
  onReply: (body: string) => Promise<string | null>;
  onReplyComplete: () => void;
  onFollowUp: (ticket: SupportTicketThreadDto) => void;
  onCloseTicket: () => Promise<void>;
}) {
  const isClosed = data.status === SupportTicketStatus.Closed;
  const [confirmClose, setConfirmClose] = useState(false);

  return (
    <>
      <SheetHeader className="border-border border-b">
        <div className="flex items-start justify-between gap-3 pr-8">
          <SheetTitle className="min-w-0 break-words">{data.subject}</SheetTitle>
          <Badge variant={statusBadgeVariant[data.status]} className="shrink-0">
            {statusLabel[data.status]}
          </Badge>
        </div>
        <SheetDescription>
          Opened {new Date(data.createdAt).toLocaleDateString()} ·{' '}
          {data.messages.length === 1 ? '1 message' : `${data.messages.length} messages`}
        </SheetDescription>
      </SheetHeader>

      <div
        className="flex-1 space-y-4 overflow-y-auto overscroll-contain px-4 py-2"
        aria-live="polite"
        aria-relevant="additions"
      >
        {data.messages.map((message) => {
          const fromAgent = message.authorRole === SupportMessageAuthor.Agent;
          return (
            <div
              key={message.id}
              className={fromAgent ? 'flex justify-start' : 'flex justify-end'}
            >
              <div
                className={
                  'max-w-[85%] space-y-1 rounded-xl px-3 py-2 text-sm ' +
                  (fromAgent
                    ? 'bg-muted text-foreground'
                    : 'bg-primary/10 text-foreground')
                }
              >
                <p className="text-muted-foreground text-xs font-medium">
                  {fromAgent ? 'Support' : 'You'} ·{' '}
                  {new Date(message.createdAt).toLocaleString()}
                </p>
                <p className="whitespace-pre-wrap break-words">{message.body}</p>
                {message.attachments.length > 0 && (
                  <ul className="space-y-0.5 pt-1 text-xs">
                    {message.attachments.map((a) => (
                      <li key={a.id}>
                        <a
                          href={supportAttachmentDownloadUrl(a.id)}
                          className="text-primary inline-flex items-center gap-1 hover:underline"
                        >
                          <Paperclip aria-hidden="true" className="h-3 w-3 shrink-0" />
                          <span className="break-all">{a.fileName}</span>
                        </a>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
          );
        })}
      </div>

      <div className="border-border border-t p-4">
        {isClosed ? (
          <Alert>
            <AlertDescription>
              This ticket is closed. Start a follow-up if the issue continues.
            </AlertDescription>
            <Button size="sm" variant="outline" onClick={() => onFollowUp(data)}>
              Start a follow-up
            </Button>
          </Alert>
        ) : (
          <div className="space-y-3">
            <ReplyComposer onSubmit={onReply} onComplete={onReplyComplete} />
            <div className="flex justify-start border-t border-border pt-3">
              <Button
                variant="ghost"
                size="sm"
                className="text-muted-foreground"
                onClick={() => setConfirmClose(true)}
              >
                Close ticket
              </Button>
            </div>
          </div>
        )}
      </div>

      <AlertDialog open={confirmClose} onOpenChange={setConfirmClose}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Close this ticket?</AlertDialogTitle>
            <AlertDialogDescription>
              A closed ticket can&apos;t take new replies. You can always start a follow-up later
              if the issue comes back.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={() => void onCloseTicket()}>Close ticket</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}

function ThreadSkeleton() {
  return (
    <>
      <SheetHeader className="border-border border-b">
        <Skeleton className="h-5 w-2/3" />
        <Skeleton className="h-3 w-1/3" />
      </SheetHeader>
      <div className="flex-1 space-y-4 px-4 py-2">
        <Skeleton className="h-16 w-3/4" />
        <Skeleton className="ml-auto h-16 w-3/4" />
      </div>
    </>
  );
}

function ThreadError({ onClose }: { onClose: () => void }) {
  return (
    <>
      <SheetHeader className="border-border border-b">
        <SheetTitle>Conversation unavailable</SheetTitle>
      </SheetHeader>
      <div className="space-y-4 p-4">
        <p className="text-muted-foreground text-sm">
          We couldn&apos;t load this conversation. It may have been removed.
        </p>
        <Button variant="outline" size="sm" onClick={onClose}>
          Back to tickets
        </Button>
      </div>
    </>
  );
}
