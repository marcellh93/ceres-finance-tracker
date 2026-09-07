import { apiFetch } from '../../lib/api-client';
import {
  SupportTicketStatus,
  SupportTicketPriority,
  SupportMessageAuthor,
  type SupportAttachmentDto,
} from '../support/support-api';

// Stage 12.5.2 — the admin (operator) read surface. Reuses the support wire enums +
// attachment DTO; the write path (reply / set status) is the EXISTING operator endpoint.

// ---------- URL builders ----------

export const ADMIN_TICKETS_URL = '/api/admin/support/tickets';
export const adminTicketUrl = (id: string) => `/api/admin/support/tickets/${id}`;
/** Existing operator reply/status endpoint (Stage 12.6). */
export const adminTicketMessagesUrl = (id: string) => `/api/admin/support/tickets/${id}/messages`;

// ---------- DTOs (mirror SupportAdminApiController records) ----------

/** Mirrors AdminTicketListItemDto — a rollup row plus the owner identity. */
export type AdminTicketListItemDto = {
  id: string;
  subject: string;
  status: SupportTicketStatus;
  priority: SupportTicketPriority;
  precedingTicketId: string | null;
  createdAt: string;
  updatedAt: string;
  messageCount: number;
  lastMessageAt: string;
  ownerUserId: string;
  ownerEmail: string;
};

/** Mirrors AdminTicketListResponse — the offset-paginated envelope. */
export type AdminTicketListResponse = {
  items: AdminTicketListItemDto[];
  page: number;
  pageSize: number;
  total: number;
};

/** Mirrors AdminMessageDto. */
export type AdminMessageDto = {
  id: string;
  authorRole: SupportMessageAuthor;
  body: string;
  createdAt: string;
  attachments: SupportAttachmentDto[];
};

/** Mirrors AdminTicketThreadDto — the full thread plus owner identity. */
export type AdminTicketThreadDto = {
  id: string;
  subject: string;
  status: SupportTicketStatus;
  priority: SupportTicketPriority;
  ownerUserId: string;
  ownerEmail: string;
  messages: AdminMessageDto[];
};

export type AdminTicketListParams = {
  page?: number;
  pageSize?: number;
  status?: SupportTicketStatus;
  priority?: SupportTicketPriority;
};

export function listAdminTickets(params: AdminTicketListParams = {}) {
  const q = new URLSearchParams();
  if (params.page != null) q.set('page', String(params.page));
  if (params.pageSize != null) q.set('pageSize', String(params.pageSize));
  if (params.status != null) q.set('status', String(params.status));
  if (params.priority != null) q.set('priority', String(params.priority));
  const qs = q.toString();
  return apiFetch<AdminTicketListResponse>(qs ? `${ADMIN_TICKETS_URL}?${qs}` : ADMIN_TICKETS_URL);
}

export function getAdminTicket(id: string) {
  return apiFetch<AdminTicketThreadDto>(adminTicketUrl(id));
}
