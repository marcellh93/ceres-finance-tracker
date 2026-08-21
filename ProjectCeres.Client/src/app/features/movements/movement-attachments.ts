import {
  type AttachmentDto,
  TRANSACTION_ATTACHMENTS_URL,
  TRANSACTION_ATTACHMENT_BY_ID_URL,
  TRANSFER_ATTACHMENTS_URL,
  TRANSFER_ATTACHMENT_BY_ID_URL,
} from './movements-api';
import { apiFetch } from '../../lib/api-client';

export type AttachmentParentType = 'Transaction' | 'Transfer';

export async function uploadAttachment(
  parentType: AttachmentParentType,
  parentId: string,
  file: File,
): Promise<AttachmentDto> {
  const url = parentType === 'Transaction'
    ? TRANSACTION_ATTACHMENTS_URL(parentId)
    : TRANSFER_ATTACHMENTS_URL(parentId);

  const form = new FormData();
  form.append('file', file);

  // FormData is passed through unserialised by apiFetch, which also omits the
  // JSON Content-Type so the browser sets its own multipart boundary.
  const response = await apiFetch<AttachmentDto>(url, { method: 'POST', body: form });

  if (response.ok && response.status === 201 && response.data) {
    return response.data;
  }

  if (!response.ok && response.status === 422) {
    const message =
      ('formError' in response ? response.formError : response.message) ??
      'Could not upload the file.';
    throw new Error(message);
  }

  throw new Error("Couldn't upload the file. Try again.");
}

export async function deleteAttachment(
  parentType: AttachmentParentType,
  attachmentId: string,
): Promise<void> {
  const url = parentType === 'Transaction'
    ? TRANSACTION_ATTACHMENT_BY_ID_URL(attachmentId)
    : TRANSFER_ATTACHMENT_BY_ID_URL(attachmentId);

  const response = await apiFetch(url, { method: 'DELETE' });

  if (response.ok && response.status === 204) return;
  throw new Error("Couldn't delete the attachment. Try again.");
}
