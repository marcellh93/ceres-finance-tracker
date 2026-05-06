import { useRef, useState } from 'react';
import { toast } from 'sonner';
import { Upload, FileText } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Button } from '@/components/ui/button';

type Props = {
  /** Comma-separated list, e.g. ".csv,.xlsx" */
  accept: string;
  /** Maximum file size in bytes. Files larger than this are rejected with a toast. */
  maxBytes: number;
  selectedFile: File | null;
  onFile: (file: File) => void;
  /** Optional id for the hidden input — pair with a Label for screen readers. */
  inputId?: string;
};

function isExtensionAccepted(filename: string, accept: string): boolean {
  const accepted = accept
    .split(',')
    .map((s) => s.trim().toLowerCase())
    .filter(Boolean);
  if (accepted.length === 0) return true;
  const lower = filename.toLowerCase();
  return accepted.some((ext) => lower.endsWith(ext));
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function FileDropzone({
  accept,
  maxBytes,
  selectedFile,
  onFile,
  inputId = 'file-dropzone-input',
}: Props) {
  const [dragOver, setDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  function handleFile(file: File) {
    if (!isExtensionAccepted(file.name, accept)) {
      toast.error(`Unsupported file type. Accepted: ${accept}.`);
      return;
    }
    if (file.size > maxBytes) {
      toast.error(`File exceeds the ${formatBytes(maxBytes)} limit.`);
      return;
    }
    onFile(file);
  }

  function handleDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setDragOver(false);
    const file = e.dataTransfer.files?.[0];
    if (file) handleFile(file);
  }

  function handleClick() {
    inputRef.current?.click();
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLDivElement>) {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      handleClick();
    }
  }

  return (
    <div className="space-y-2">
      <div
        role="button"
        tabIndex={0}
        aria-label="Upload bank statement file"
        aria-describedby={`${inputId}-hint`}
        onClick={handleClick}
        onKeyDown={handleKeyDown}
        onDragOver={(e) => {
          e.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={handleDrop}
        className={cn(
          'flex flex-col items-center justify-center gap-2 rounded-md border-2 border-dashed px-6 py-10 text-center cursor-pointer transition-colors',
          'focus:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
          dragOver
            ? 'border-primary bg-primary/5'
            : 'border-input bg-muted/40 hover:bg-muted/60',
        )}
      >
        {selectedFile ? (
          <>
            <FileText className="h-8 w-8 text-muted-foreground" aria-hidden />
            <div className="text-sm font-medium">{selectedFile.name}</div>
            <div className="text-xs text-muted-foreground">{formatBytes(selectedFile.size)}</div>
            <Button
              type="button"
              variant="link"
              size="sm"
              onClick={(e) => {
                e.stopPropagation();
                handleClick();
              }}
            >
              Replace file
            </Button>
          </>
        ) : (
          <>
            <Upload className="h-8 w-8 text-muted-foreground" aria-hidden />
            <div className="text-sm">
              <span className="font-medium">Drop a file here</span>
              <span className="text-muted-foreground"> or click to browse</span>
            </div>
            <div className="text-xs text-muted-foreground" id={`${inputId}-hint`}>
              CSV or Excel (.xlsx), up to {formatBytes(maxBytes)}.
            </div>
          </>
        )}
        <input
          ref={inputRef}
          id={inputId}
          type="file"
          className="sr-only"
          accept={accept}
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) handleFile(file);
            // Reset so re-selecting the same file fires onChange again.
            e.target.value = '';
          }}
        />
      </div>
    </div>
  );
}
