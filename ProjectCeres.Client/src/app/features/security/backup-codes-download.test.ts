import { describe, expect, it, vi } from 'vitest';
import { synthesizeBackupCodesTxt, downloadBackupCodes } from './backup-codes-download';

describe('synthesizeBackupCodesTxt', () => {
  it('returns a string containing all codes + email + date', () => {
    const codes = ['AAAA-BBBB-CCCC-DDDD', 'EEEE-FFFF-GGGG-HHHH'];
    const text = synthesizeBackupCodesTxt(codes, 'a@b.test');
    expect(text).toContain('a@b.test');
    expect(text).toContain('AAAA-BBBB-CCCC-DDDD');
    expect(text).toContain('EEEE-FFFF-GGGG-HHHH');
    // Date in YYYY-MM-DD shape
    expect(text).toMatch(/\d{4}-\d{2}-\d{2}/);
  });
});

describe('downloadBackupCodes', () => {
  it('creates a Blob with text/plain MIME type', () => {
    let capturedBlob: Blob | null = null;
    const createUrlSpy = vi.spyOn(URL, 'createObjectURL').mockImplementation((blob: Blob | MediaSource) => {
      capturedBlob = blob as Blob;
      return 'blob:test';
    });
    const revokeSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});

    downloadBackupCodes(['XXXX-YYYY-ZZZZ-WWWW'], 'x@y.test', 'ceres-backup-codes.txt');

    expect(capturedBlob).not.toBeNull();
    expect(capturedBlob!.type).toBe('text/plain;charset=utf-8');

    createUrlSpy.mockRestore();
    revokeSpy.mockRestore();
  });

  it('revokes the object URL after the click', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
    const revokeSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});

    downloadBackupCodes(['ZZZZ-WWWW-VVVV-UUUU'], 'z@w.test', 'codes.txt');

    // setTimeout(...0...) → wait a tick
    await new Promise((r) => setTimeout(r, 5));
    expect(revokeSpy).toHaveBeenCalledWith('blob:test');

    vi.restoreAllMocks();
  });
});
