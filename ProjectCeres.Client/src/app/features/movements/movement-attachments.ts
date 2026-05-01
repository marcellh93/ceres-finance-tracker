import {
  type AttachmentDto,
  TRANSACTION_ATTACHMENTS_URL,
  TRANSACTION_ATTACHMENT_BY_ID_URL,
  TRANSFER_ATTACHMENTS_URL,
  TRANSFER_ATTACHMENT_BY_ID_URL,
} from './movements-api';

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

  const response = await fetch(url, { method: 'POST', body: form });

  if (response.status === 201) {
    return (await response.json()) as AttachmentDto;
  }

  if (response.status === 422) {
    const body = await response.json().catch(() => null);
    const message = body?.error?.message ?? 'Could not upload the file.';
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

  const response = await fetch(url, { method: 'DELETE' });

  if (response.status === 204) return;
  throw new Error("Couldn't delete the attachment. Try again.");
}
