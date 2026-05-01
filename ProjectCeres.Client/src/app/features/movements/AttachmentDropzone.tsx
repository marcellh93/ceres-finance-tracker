import { useEffect, useRef, useState } from 'react';
import { toast } from 'sonner';
import { Upload, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import type { AttachmentDto } from './movements-api';
import {
  type AttachmentParentType,
  deleteAttachment,
  uploadAttachment,
} from './movement-attachments';

type AttachmentDropzoneProps = {
  parentType: AttachmentParentType;
  parentId: string;
  initialAttachments: AttachmentDto[];
  /** Optional file to auto-upload on mount (the Create→Edit hand-off). */
  pendingFile?: File | null;
  /** Called whenever the attachment list changes (added/removed). */
  onChange?: (attachments: AttachmentDto[]) => void;
};

type PendingUpload = {
  tempId: string;
  fileName: string;
  sizeBytes: number;
};

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString();
  } catch {
    return iso;
  }
}

function makeTempId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return `tmp-${crypto.randomUUID()}`;
  }
  return `tmp-${Math.random().toString(36).slice(2)}-${Date.now()}`;
}

export function AttachmentDropzone({
  parentType,
  parentId,
  initialAttachments,
  pendingFile,
  onChange,
}: AttachmentDropzoneProps) {
  // Defensive runtime guard — see component contract.
  if ((parentType as string) === 'LiabilityPayment') {
    throw new Error('AttachmentDropzone does not support LiabilityPayment parents.');
  }

  const [attachments, setAttachments] = useState<AttachmentDto[]>(initialAttachments);
  const [pending, setPending] = useState<PendingUpload[]>([]);
  const [isDragging, setIsDragging] = useState(false);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const pendingFileHandledRef = useRef(false);

  function notifyChange(next: AttachmentDto[]) {
    onChange?.(next);
  }

  async function uploadOne(file: File, options?: { announceToast?: boolean }) {
    const tempId = makeTempId();
    setPending((prev) => [
      ...prev,
      { tempId, fileName: file.name, sizeBytes: file.size },
    ]);
    try {
      const result = await uploadAttachment(parentType, parentId, file);
      setAttachments((prev) => {
        const next = [...prev, result];
        notifyChange(next);
        return next;
      });
      if (options?.announceToast) {
        toast.success('Attachment uploaded.');
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : "Couldn't upload the file.";
      toast.error(message);
    } finally {
      setPending((prev) => prev.filter((p) => p.tempId !== tempId));
    }
  }

  function handleFiles(files: FileList | File[] | null | undefined) {
    if (!files) return;
    const list = Array.from(files);
    for (const file of list) {
      void uploadOne(file);
    }
  }

  // pendingFile auto-upload (run once on mount).
  useEffect(() => {
    if (pendingFileHandledRef.current) return;
    if (!pendingFile) return;
    pendingFileHandledRef.current = true;
    void uploadOne(pendingFile, { announceToast: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function handleConfirmDelete() {
    const id = confirmDeleteId;
    if (!id) return;
    setConfirmDeleteId(null);
    void (async () => {
      try {
        await deleteAttachment(parentType, id);
        setAttachments((prev) => {
          const next = prev.filter((a) => a.id !== id);
          notifyChange(next);
          return next;
        });
      } catch (err) {
        const message = err instanceof Error ? err.message : "Couldn't delete the attachment.";
        toast.error(message);
      }
    })();
  }

  return (
    <div className="space-y-3">
      {pending.length > 0 && (
        <div className="rounded-md border border-dashed border-primary/50 bg-primary/5 px-3 py-2 text-sm text-primary">
          Uploading {pending.length} {pending.length === 1 ? 'file' : 'files'}…
        </div>
      )}

      <div
        data-testid="attachment-dropzone"
        onDragOver={(e) => {
          e.preventDefault();
          setIsDragging(true);
        }}
        onDragLeave={(e) => {
          e.preventDefault();
          setIsDragging(false);
        }}
        onDrop={(e) => {
          e.preventDefault();
          setIsDragging(false);
          handleFiles(e.dataTransfer?.files);
        }}
        className={[
          'flex flex-col items-center justify-center gap-2 rounded-md border-2 border-dashed px-4 py-6 text-center transition-colors',
          isDragging ? 'border-primary bg-primary/5' : 'border-muted-foreground/30',
        ].join(' ')}
      >
        <Upload className="h-5 w-5 text-muted-foreground" aria-hidden="true" />
        <p className="text-sm text-muted-foreground">
          Drag &amp; drop files here, or
        </p>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => fileInputRef.current?.click()}
          aria-label="Choose files to upload"
        >
          Choose files
        </Button>
        <input
          ref={fileInputRef}
          data-testid="attachment-file-input"
          type="file"
          multiple
          className="hidden"
          aria-hidden="true"
          tabIndex={-1}
          onChange={(e) => {
            handleFiles(e.target.files);
            // reset so the same file can be re-picked later
            e.target.value = '';
          }}
        />
      </div>

      {(attachments.length > 0 || pending.length > 0) && (
        <ul className="divide-y rounded-md border">
          {attachments.map((a) => (
            <li key={a.id} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
              <div className="min-w-0 flex-1">
                <div className="truncate font-medium">{a.fileName}</div>
                <div className="text-xs text-muted-foreground">
                  {formatSize(a.sizeBytes)} · {formatDate(a.uploadedAt)}
                </div>
              </div>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                aria-label={`Delete attachment ${a.fileName}`}
                onClick={() => setConfirmDeleteId(a.id)}
              >
                <X className="h-4 w-4" />
              </Button>
            </li>
          ))}
          {pending.map((p) => (
            <li
              key={p.tempId}
              className="flex items-center justify-between gap-3 px-3 py-2 text-sm opacity-70"
            >
              <div className="min-w-0 flex-1">
                <div className="truncate font-medium">{p.fileName}</div>
                <div className="text-xs text-muted-foreground">
                  {formatSize(p.sizeBytes)} · Uploading…
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}

      <AlertDialog
        open={confirmDeleteId !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmDeleteId(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete this attachment?</AlertDialogTitle>
            <AlertDialogDescription>This action cannot be undone.</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleConfirmDelete}>Delete</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
