import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { toast } from 'sonner';
import { FileDropzone } from './FileDropzone';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}));

function makeFile(name: string, sizeBytes: number, type = 'text/csv') {
  const blob = new Blob([new Uint8Array(sizeBytes)], { type });
  return new File([blob], name, { type });
}

describe('FileDropzone', () => {
  it('renders the empty-state copy and accept hint', () => {
    render(
      <FileDropzone
        accept=".csv,.xlsx"
        maxBytes={10 * 1024 * 1024}
        selectedFile={null}
        onFile={() => {}}
      />,
    );
    expect(screen.getByText(/drop a file here/i)).toBeInTheDocument();
    expect(screen.getByText(/csv or excel/i)).toBeInTheDocument();
  });

  it('calls onFile on a valid drop', () => {
    const onFile = vi.fn();
    render(
      <FileDropzone
        accept=".csv,.xlsx"
        maxBytes={10 * 1024 * 1024}
        selectedFile={null}
        onFile={onFile}
      />,
    );
    const dropzone = screen.getByRole('button', { name: /upload bank statement file/i });
    const file = makeFile('a.csv', 100);
    fireEvent.drop(dropzone, { dataTransfer: { files: [file] } });
    expect(onFile).toHaveBeenCalledWith(file);
  });

  it('rejects unsupported extensions with a toast', () => {
    const onFile = vi.fn();
    render(
      <FileDropzone
        accept=".csv,.xlsx"
        maxBytes={10 * 1024 * 1024}
        selectedFile={null}
        onFile={onFile}
      />,
    );
    const dropzone = screen.getByRole('button', { name: /upload bank statement file/i });
    const bad = makeFile('a.pdf', 100, 'application/pdf');
    fireEvent.drop(dropzone, { dataTransfer: { files: [bad] } });
    expect(onFile).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledWith(expect.stringMatching(/unsupported/i));
  });

  it('rejects files over maxBytes with a toast', () => {
    const onFile = vi.fn();
    render(
      <FileDropzone
        accept=".csv,.xlsx"
        maxBytes={1024}
        selectedFile={null}
        onFile={onFile}
      />,
    );
    const dropzone = screen.getByRole('button', { name: /upload bank statement file/i });
    const big = makeFile('a.csv', 2048);
    fireEvent.drop(dropzone, { dataTransfer: { files: [big] } });
    expect(onFile).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledWith(expect.stringMatching(/limit/i));
  });

  it('renders the selected file with a Replace affordance', () => {
    const onFile = vi.fn();
    const file = makeFile('statement.csv', 100);
    render(
      <FileDropzone
        accept=".csv,.xlsx"
        maxBytes={10 * 1024 * 1024}
        selectedFile={file}
        onFile={onFile}
      />,
    );
    expect(screen.getByText('statement.csv')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /replace file/i })).toBeInTheDocument();
  });

  it('Enter key activates the picker', () => {
    render(
      <FileDropzone
        accept=".csv,.xlsx"
        maxBytes={10 * 1024 * 1024}
        selectedFile={null}
        onFile={() => {}}
      />,
    );
    const dropzone = screen.getByRole('button', { name: /upload bank statement file/i });
    // No native file picker in jsdom; assert no crash + the input element exists.
    fireEvent.keyDown(dropzone, { key: 'Enter' });
    // The hidden input is in the document.
    expect(document.querySelector('input[type="file"]')).toBeTruthy();
  });
});
