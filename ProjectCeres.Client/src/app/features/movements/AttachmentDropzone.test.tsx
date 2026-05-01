import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AttachmentDropzone } from './AttachmentDropzone';
import type { AttachmentDto } from './movements-api';

vi.mock('./movement-attachments', () => ({
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

import { deleteAttachment, uploadAttachment } from './movement-attachments';
import { toast } from 'sonner';

const existing: AttachmentDto = {
  id: 'a1',
  fileName: 'receipt.pdf',
  sizeBytes: 2048,
  contentType: 'application/pdf',
  uploadedAt: '2025-01-15T10:00:00Z',
};

const newAttachment: AttachmentDto = {
  id: 'a2',
  fileName: 'invoice.png',
  sizeBytes: 4096,
  contentType: 'image/png',
  uploadedAt: '2025-02-01T12:00:00Z',
};

beforeEach(() => {
  vi.mocked(uploadAttachment).mockReset();
  vi.mocked(deleteAttachment).mockReset();
  vi.mocked(toast.success).mockReset();
  vi.mocked(toast.error).mockReset();
});

afterEach(() => {
  vi.resetAllMocks();
});

function makeFile(name = 'newfile.png', size = 4096, type = 'image/png'): File {
  const file = new File([new Uint8Array(size)], name, { type });
  return file;
}

describe('AttachmentDropzone', () => {
  it('renders initialAttachments with file name, size, and delete button', () => {
    render(
      <AttachmentDropzone
        parentType="Transaction"
        parentId="tx1"
        initialAttachments={[existing]}
      />,
    );

    expect(screen.getByText('receipt.pdf')).toBeInTheDocument();
    expect(screen.getByText(/2\.0\s*KB/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /delete attachment/i })).toBeInTheDocument();
  });

  it('drag-and-drop a file calls uploadAttachment and adds the new item; onChange fires', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue(newAttachment);
    const onChange = vi.fn();
    render(
      <AttachmentDropzone
        parentType="Transaction"
        parentId="tx1"
        initialAttachments={[existing]}
        onChange={onChange}
      />,
    );

    const zone = screen.getByTestId('attachment-dropzone');
    const file = makeFile();

    fireEvent.dragOver(zone, { dataTransfer: { files: [file] } });
    fireEvent.drop(zone, { dataTransfer: { files: [file] } });

    await waitFor(() => {
      expect(uploadAttachment).toHaveBeenCalledWith('Transaction', 'tx1', file);
    });

    await waitFor(() => {
      expect(screen.getByText('invoice.png')).toBeInTheDocument();
    });

    expect(onChange).toHaveBeenCalled();
    const lastCall = onChange.mock.calls[onChange.mock.calls.length - 1][0];
    expect(lastCall).toEqual(expect.arrayContaining([existing, newAttachment]));
  });

  it('file picker fallback uploads the chosen file', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue(newAttachment);
    const onChange = vi.fn();
    render(
      <AttachmentDropzone
        parentType="Transfer"
        parentId="tr1"
        initialAttachments={[]}
        onChange={onChange}
      />,
    );

    const file = makeFile();
    const input = screen.getByTestId('attachment-file-input') as HTMLInputElement;

    fireEvent.change(input, { target: { files: [file] } });

    await waitFor(() => {
      expect(uploadAttachment).toHaveBeenCalledWith('Transfer', 'tr1', file);
    });
    await waitFor(() => {
      expect(screen.getByText('invoice.png')).toBeInTheDocument();
    });
    expect(onChange).toHaveBeenCalled();
  });

  it('upload failure shows toast.error and removes placeholder; existing items intact', async () => {
    vi.mocked(uploadAttachment).mockRejectedValue(new Error('Could not upload the file.'));
    render(
      <AttachmentDropzone
        parentType="Transaction"
        parentId="tx1"
        initialAttachments={[existing]}
      />,
    );

    const file = makeFile('bad.png');
    const input = screen.getByTestId('attachment-file-input') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [file] } });

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith('Could not upload the file.');
    });

    expect(screen.queryByText('bad.png')).not.toBeInTheDocument();
    expect(screen.getByText('receipt.pdf')).toBeInTheDocument();
  });

  it('clicking delete and confirming calls deleteAttachment and removes the item', async () => {
    vi.mocked(deleteAttachment).mockResolvedValue();
    const onChange = vi.fn();
    render(
      <AttachmentDropzone
        parentType="Transaction"
        parentId="tx1"
        initialAttachments={[existing]}
        onChange={onChange}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /delete attachment/i }));
    await screen.findByText('Delete this attachment?');

    const confirmButtons = screen.getAllByRole('button', { name: /delete/i });
    fireEvent.click(confirmButtons[confirmButtons.length - 1]);

    await waitFor(() => {
      expect(deleteAttachment).toHaveBeenCalledWith('Transaction', 'a1');
    });

    await waitFor(() => {
      expect(screen.queryByText('receipt.pdf')).not.toBeInTheDocument();
    });
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it('clicking delete then cancel does not call deleteAttachment; item still present', async () => {
    render(
      <AttachmentDropzone
        parentType="Transaction"
        parentId="tx1"
        initialAttachments={[existing]}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /delete attachment/i }));
    await screen.findByText('Delete this attachment?');

    const cancel = screen.getByRole('button', { name: /cancel/i });
    fireEvent.click(cancel);

    await waitFor(() => {
      expect(deleteAttachment).not.toHaveBeenCalled();
    });
    expect(screen.getByText('receipt.pdf')).toBeInTheDocument();
  });

  it('pendingFile prop on mount triggers an immediate upload and toast.success', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue(newAttachment);
    const file = makeFile('pending.png');
    render(
      <AttachmentDropzone
        parentType="Transaction"
        parentId="tx1"
        initialAttachments={[]}
        pendingFile={file}
      />,
    );

    await waitFor(() => {
      expect(uploadAttachment).toHaveBeenCalledWith('Transaction', 'tx1', file);
    });
    await waitFor(() => {
      expect(toast.success).toHaveBeenCalled();
    });
  });

  it('multiple uploads in flight all show "Uploading…" UI simultaneously', async () => {
    vi.mocked(uploadAttachment).mockImplementation(() => new Promise(() => {}));
    render(
      <AttachmentDropzone
        parentType="Transaction"
        parentId="tx1"
        initialAttachments={[]}
      />,
    );

    const f1 = makeFile('one.png');
    const f2 = makeFile('two.png');
    const input = screen.getByTestId('attachment-file-input') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [f1, f2] } });

    await waitFor(() => {
      expect(screen.getByText(/uploading 2 files/i)).toBeInTheDocument();
    });
    expect(screen.getByText('one.png')).toBeInTheDocument();
    expect(screen.getByText('two.png')).toBeInTheDocument();
  });
});
