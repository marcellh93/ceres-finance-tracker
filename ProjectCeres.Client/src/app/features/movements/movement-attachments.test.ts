import { describe, expect, it, vi, beforeEach } from 'vitest';
import { uploadAttachment, deleteAttachment } from './movement-attachments';

let mockFetch: ReturnType<typeof vi.fn>;
beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
});

describe('uploadAttachment', () => {
  it('POSTs multipart/form-data to the transaction endpoint', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 201,
      json: async () => ({
        id: 'a1', fileName: 'r.png', sizeBytes: 100,
        contentType: 'image/png', uploadedAt: '2026-04-30T10:00:00Z',
      }),
    });
    const file = new File([new Uint8Array([0x89, 0x50])], 'r.png', { type: 'image/png' });

    const result = await uploadAttachment('Transaction', 'tx-id', file);

    expect(mockFetch).toHaveBeenCalledWith(
      '/api/transactions/tx-id/attachments',
      expect.objectContaining({ method: 'POST', body: expect.any(FormData) }),
    );
    expect(result.fileName).toBe('r.png');
  });

  it('POSTs to the transfer endpoint when parentType is Transfer', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true, status: 201,
      json: async () => ({ id: 'a2', fileName: 'r.png', sizeBytes: 100, contentType: 'image/png', uploadedAt: '2026-04-30T10:00:00Z' }),
    });
    const file = new File([new Uint8Array([0x89])], 'r.png', { type: 'image/png' });
    await uploadAttachment('Transfer', 'tr-id', file);
    expect(mockFetch).toHaveBeenCalledWith('/api/transfers/tr-id/attachments', expect.anything());
  });

  it('rejects with the error.message on 422', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false, status: 422,
      json: async () => ({
        error: { code: 'VALIDATION_ERROR', message: 'File type not allowed.', details: [] },
      }),
    });
    const file = new File([new Uint8Array([0x00])], 'r.exe', { type: 'application/octet-stream' });
    await expect(uploadAttachment('Transaction', 'tx-id', file))
      .rejects.toThrow('File type not allowed.');
  });

  it('rejects with a generic message on other failures', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 500, json: async () => ({}) });
    const file = new File([new Uint8Array([0x00])], 'r.png', { type: 'image/png' });
    await expect(uploadAttachment('Transaction', 'tx-id', file))
      .rejects.toThrow(/upload/i);
  });
});

describe('deleteAttachment', () => {
  it('DELETEs the transaction attachment endpoint', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, status: 204 });
    await deleteAttachment('Transaction', 'att-1');
    expect(mockFetch).toHaveBeenCalledWith('/api/transactions/attachments/att-1', expect.objectContaining({ method: 'DELETE' }));
  });

  it('DELETEs the transfer attachment endpoint', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, status: 204 });
    await deleteAttachment('Transfer', 'att-2');
    expect(mockFetch).toHaveBeenCalledWith('/api/transfers/attachments/att-2', expect.objectContaining({ method: 'DELETE' }));
  });

  it('rejects on non-204', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 404 });
    await expect(deleteAttachment('Transaction', 'gone')).rejects.toThrow(/delete/i);
  });
});
