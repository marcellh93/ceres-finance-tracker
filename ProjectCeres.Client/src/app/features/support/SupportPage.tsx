import { useCallback } from 'react';
import { MessageSquarePlus, ChevronRight } from 'lucide-react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { useDocumentTitle } from '../../lib/use-document-title';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { useApi } from '../../lib/use-api';
import { SupportThreadSheet } from './SupportThreadSheet';
import { NewTicketSheet } from './NewTicketSheet';
import {
  SUPPORT_TICKETS_URL,
  statusBadgeVariant,
  statusLabel,
  type SupportTicketListItemDto,
  type SupportTicketThreadDto,
} from './support-api';

/**
 * Support tickets: a list of the caller's own tickets, each opening its
 * conversation in a URL-reflected slide-in sheet.
 *
 * Routing carries the overlay state so the URL and the UI never disagree:
 *   /support            → list only
 *   /support/new        → the compose sheet
 *   /support/<ticketId> → that ticket's thread sheet
 * Closing a sheet navigates back to /support; a deep link opens straight into it.
 */
export function SupportPage() {
  useDocumentTitle('Support');
  const navigate = useNavigate();
  const location = useLocation();
  const { ticketId } = useParams<{ ticketId: string }>();

  const list = useApi<SupportTicketListItemDto[]>(SUPPORT_TICKETS_URL);

  // /support/new matches the STATIC route (no :ticketId param), so read the
  // compose intent from the path, not from params. A ticketId is present only
  // on the dynamic /support/:ticketId route.
  const isNew = location.pathname === '/support/new';
  const openThreadId = ticketId ?? null;

  const closeSheet = useCallback(() => navigate('/support'), [navigate]);
  const refetchList = useCallback(() => list.refetch(), [list]);

  const startFollowUp = useCallback(
    (ticket: SupportTicketThreadDto) => {
      // The compose sheet reads the follow-up context from navigation state so a
      // deep link to /support/new stays a plain new ticket.
      navigate('/support/new', {
        state: { precedingTicketId: ticket.id, subject: ticket.subject },
      });
    },
    [navigate],
  );

  const hasData = list.data !== undefined;
  const showSkeleton = useDelayedLoading(list.loading && !hasData);

  let state: DataTransitionState;
  if (showSkeleton && !hasData) state = 'skeleton';
  else if (list.error && !hasData) state = 'error';
  else state = 'data';

  const tickets = list.data;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Support</h1>
          <p className="text-muted-foreground mt-1 text-sm">
            Your support tickets and their conversations.
          </p>
        </div>
        <Button size="sm" onClick={() => navigate('/support/new')}>
          <MessageSquarePlus aria-hidden="true" />
          New ticket
        </Button>
      </div>

      <DataTransition
        state={state}
        skeleton={
          <Card>
            <CardContent className="space-y-3 pt-6">
              <Skeleton className="h-14 w-full" />
              <Skeleton className="h-14 w-full" />
            </CardContent>
          </Card>
        }
        error={
          <Card>
            <CardContent className="space-y-4 pt-6">
              <p className="text-muted-foreground text-sm">
                {list.error?.message ?? 'Could not load your tickets.'}
              </p>
              <Button variant="outline" onClick={() => list.refetch()}>
                Try again
              </Button>
            </CardContent>
          </Card>
        }
      >
        <Card>
          <CardHeader>
            <CardTitle>
              {tickets === undefined
                ? 'Tickets'
                : tickets.length === 1
                  ? '1 ticket'
                  : `${tickets.length} tickets`}
            </CardTitle>
          </CardHeader>
          <CardContent>
            {tickets !== undefined && tickets.length === 0 ? (
              <div className="py-8 text-center">
                <p className="text-muted-foreground text-sm">
                  You haven&apos;t opened any support tickets yet.
                </p>
                <Button
                  variant="outline"
                  size="sm"
                  className="mt-4"
                  onClick={() => navigate('/support/new')}
                >
                  Open your first ticket
                </Button>
              </div>
            ) : (
              <ul className="divide-border divide-y">
                {tickets?.map((ticket) => (
                  <li key={ticket.id}>
                    <button
                      type="button"
                      onClick={() => navigate(`/support/${ticket.id}`)}
                      className="hover:bg-muted/50 focus-visible:ring-ring -mx-2 flex w-[calc(100%+1rem)] items-center gap-3 rounded-md px-2 py-3 text-left transition-colors focus-visible:ring-2 focus-visible:outline-none"
                    >
                      <div className="min-w-0 flex-1 space-y-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="truncate font-medium">{ticket.subject}</span>
                          <Badge variant={statusBadgeVariant[ticket.status]} className="shrink-0">
                            {statusLabel[ticket.status]}
                          </Badge>
                        </div>
                        <p className="text-muted-foreground text-sm">
                          {ticket.messageCount === 1
                            ? '1 message'
                            : `${ticket.messageCount} messages`}{' '}
                          · Last activity {new Date(ticket.lastMessageAt).toLocaleDateString()}
                        </p>
                      </div>
                      <ChevronRight
                        aria-hidden="true"
                        className="text-muted-foreground h-4 w-4 shrink-0"
                      />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      </DataTransition>

      {openThreadId && (
        <SupportThreadSheet
          ticketId={openThreadId}
          onClose={closeSheet}
          onChanged={refetchList}
          onFollowUp={startFollowUp}
        />
      )}

      {isNew && (
        <NewTicketSheet
          onClose={closeSheet}
          onCreated={(id) => {
            refetchList();
            navigate(`/support/${id}`);
          }}
          followUp={readFollowUpState(location.state)}
        />
      )}
    </div>
  );
}

/** Follow-up context, if the compose sheet was opened from a Closed thread. */
function readFollowUpState(
  state: unknown,
): { precedingTicketId: string; subject: string } | null {
  const s = state as { precedingTicketId?: string; subject?: string } | null;
  if (s?.precedingTicketId && typeof s.subject === 'string') {
    return { precedingTicketId: s.precedingTicketId, subject: s.subject };
  }
  return null;
}
