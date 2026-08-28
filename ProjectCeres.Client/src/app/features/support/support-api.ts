import { apiFetch } from '../../lib/api-client';

// ---------- URL builders ----------

export const SUPPORT_TICKETS_URL = '/api/support/tickets';
export const supportTicketUrl = (id: string) => `/api/support/tickets/${id}`;
export const supportRepliesUrl = (id: string) => `/api/support/tickets/${id}/messages`;
export const supportCloseUrl = (id: string) => `/api/support/tickets/${id}/close`;
export const supportMessageAttachmentsUrl = (messageId: string) =>
  `/api/support/messages/${messageId}/attachments`;
/** Download link for one attachment (Content-Disposition: attachment). */
export const supportAttachmentDownloadUrl = (attachmentId: string) =>
  `/api/attachments/support/${attachmentId}`;

// ---------- Enums (wire contract) ----------

// These mirror ProjectCeres/Models/SupportTicketStatus.cs etc. The API has no
// JsonStringEnumConverter registered, so System.Text.Json serializes them as
// their integer ordinals — the backend tests read `status`/`authorRole` via
// GetInt32() (SupportConversationApiTests). The ordinals ARE the DB contract;
// do not reorder.

export const SupportTicketStatus = {
  Open: 0,
  Pending: 1,
  OnHold: 2,
  Solved: 3,
  Closed: 4,
} as const;
export type SupportTicketStatus =
  (typeof SupportTicketStatus)[keyof typeof SupportTicketStatus];

export const SupportMessageAuthor = {
  User: 0,
  Agent: 1,
} as const;
export type SupportMessageAuthor =
  (typeof SupportMessageAuthor)[keyof typeof SupportMessageAuthor];

export const SupportTicketPriority = {
  Low: 0,
  Normal: 1,
  High: 2,
  Urgent: 3,
} as const;
export type SupportTicketPriority =
  (typeof SupportTicketPriority)[keyof typeof SupportTicketPriority];

// ---------- DTOs ----------

/** Mirrors SupportApiController.AttachmentDto. */
export type SupportAttachmentDto = {
  id: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  uploadedAt: string;
};

/** Mirrors SupportApiController.MessageDto — one message in the conversation. */
export type SupportMessageDto = {
  id: string;
  authorRole: SupportMessageAuthor;
  body: string;
  createdAt: string;
  attachments: SupportAttachmentDto[];
};

/**
 * Mirrors SupportApiController.TicketListItemDto — the list rollup. One row per
 * ticket with a message count and no bodies; the thread is a separate shape.
 */
export type SupportTicketListItemDto = {
  id: string;
  subject: string;
  status: SupportTicketStatus;
  priority: SupportTicketPriority;
  precedingTicketId: string | null;
  createdAt: string;
  updatedAt: string;
  messageCount: number;
  lastMessageAt: string;
};

/** Mirrors SupportApiController.TicketThreadDto — the full conversation. */
export type SupportTicketThreadDto = {
  id: string;
  subject: string;
  status: SupportTicketStatus;
  priority: SupportTicketPriority;
  precedingTicketId: string | null;
  createdAt: string;
  updatedAt: string;
  messages: SupportMessageDto[];
};

// ---------- Request bodies ----------

export type CreateTicketRequest = {
  subject: string;
  message: string;
  priority: SupportTicketPriority;
  precedingTicketId?: string | null;
};

export type ReplyRequest = { body: string };

// ---------- Attachment upload ----------

/**
 * Uploads one file to a message. The support upload endpoint answers 200 (not
 * 201 like the movements one) with the AttachmentDto. FormData rides through
 * apiFetch unserialised so the browser sets its own multipart boundary.
 */
export async function uploadSupportAttachment(
  messageId: string,
  file: File,
): Promise<SupportAttachmentDto> {
  const form = new FormData();
  form.append('file', file);
  const response = await apiFetch<SupportAttachmentDto>(
    supportMessageAttachmentsUrl(messageId),
    { method: 'POST', body: form },
  );
  if (response.ok && response.data) return response.data;
  if (!response.ok && response.status === 422) {
    const message =
      ('formError' in response ? response.formError : response.message) ??
      'Could not upload the file.';
    throw new Error(message);
  }
  throw new Error("Couldn't upload the file. Try again.");
}

// ---------- Presentation helpers ----------

/**
 * Status → Badge variant, per the Stage 12.6 brief. All five variants already
 * exist in badge.tsx / are documented in design-system.md § Status badges.
 */
export const statusBadgeVariant: Record<
  SupportTicketStatus,
  'info' | 'warning' | 'secondary' | 'success' | 'outline'
> = {
  [SupportTicketStatus.Open]: 'info',
  [SupportTicketStatus.Pending]: 'warning',
  [SupportTicketStatus.OnHold]: 'secondary',
  [SupportTicketStatus.Solved]: 'success',
  [SupportTicketStatus.Closed]: 'outline',
};

export const statusLabel: Record<SupportTicketStatus, string> = {
  [SupportTicketStatus.Open]: 'Open',
  [SupportTicketStatus.Pending]: 'Pending',
  [SupportTicketStatus.OnHold]: 'On hold',
  [SupportTicketStatus.Solved]: 'Solved',
  [SupportTicketStatus.Closed]: 'Closed',
};

export const priorityLabel: Record<SupportTicketPriority, string> = {
  [SupportTicketPriority.Low]: 'Low',
  [SupportTicketPriority.Normal]: 'Normal',
  [SupportTicketPriority.High]: 'High',
  [SupportTicketPriority.Urgent]: 'Urgent',
};
