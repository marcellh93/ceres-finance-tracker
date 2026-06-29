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
  mode?: 'create' | 'edit';
  parentType: AttachmentParentType;
  /** Required for 'edit' mode (uploads target this id). Ignored in 'create' mode. */
  parentId?: string;
  initialAttachments?: AttachmentDto[];
  /**
   * 'edit' mode: optional list of files to auto-upload on mount (the
   * Create→Edit hand-off). Each file is uploaded once and the array is
   * processed only on the first render (subsequent prop changes are ignored).
   */
  pendingFiles?: File[];
  /** 'edit' mode: fired whenever the persisted attachment list changes. */
  onChange?: (attachments: AttachmentDto[]) => void;
  /** 'create' mode: fired whenever the buffered file list changes. */
  onPendingChange?: (files: File[]) => void;
  title?: string;
  description?: string;
  /** Outer container class — lets the parent decide max-width / spacing. */
  className?: string;
  /**
   * When true, the component renders without its own card chrome (no
   * border, no bg-card, no padding) so it can sit inside another card
   * such as MovementForm without nested-card visual noise.
   */
  embedded?: boolean;
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

function formatDate(iso: string | undefined | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleDateString();
}

function makeTempId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return `tmp-${crypto.randomUUID()}`;
  }
  return `tmp-${Math.random().toString(36).slice(2)}-${Date.now()}`;
}

export function AttachmentDropzone({
  mode = 'edit',
  parentType,
  parentId,
  initialAttachments = [],
  pendingFiles,
  onChange,
  onPendingChange,
  title = 'Receipts',
  description,
  className,
  embedded = false,
}: AttachmentDropzoneProps) {
  // Defensive runtime guard — see component contract.
  if ((parentType as string) === 'LiabilityPayment') {
    throw new Error('AttachmentDropzone does not support LiabilityPayment parents.');
  }

  const [attachments, setAttachments] = useState<AttachmentDto[]>(initialAttachments);
  const [pending, setPending] = useState<PendingUpload[]>([]);
  const [buffered, setBuffered] = useState<File[]>([]);
  const [isDragging, setIsDragging] = useState(false);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const pendingFilesHandledRef = useRef(false);

  const resolvedDescription =
    description ??
    (mode === 'create'
      ? "We'll upload them after you save."
      : 'Drop files here to add them.');

  function notifyChange(next: AttachmentDto[]) {
    onChange?.(next);
  }

  function notifyPending(next: File[]) {
    onPendingChange?.(next);
  }

  async function uploadOne(file: File, options?: { announceToast?: boolean }) {
    if (!parentId) {
      // Should never happen in edit mode (parentId is required), but guard.
      return;
    }
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
    if (mode === 'create') {
      // Compute outside the reducer so we can also notify the parent without
      // calling setState during a different component's render.
      const next = [...buffered, ...list];
      setBuffered(next);
      notifyPending(next);
      return;
    }
    for (const file of list) {
      void uploadOne(file);
    }
  }

  function removeBufferedAt(index: number) {
    const next = buffered.filter((_, i) => i !== index);
    setBuffered(next);
    notifyPending(next);
  }

  // pendingFiles auto-upload (run once on mount, edit mode only).
  useEffect(() => {
    if (pendingFilesHandledRef.current) return;
    if (mode !== 'edit') return;
    if (!pendingFiles || pendingFiles.length === 0) return;
    pendingFilesHandledRef.current = true;
    for (const file of pendingFiles) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- Why: one-shot auto-upload on mount for Create→Edit hand-off; uploadOne is an async fire-and-forget that eventually calls setPending/setAttachments from within an async callback, not synchronously in the effect body.
      void uploadOne(file, { announceToast: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- Why: intentionally runs once on mount to auto-upload pendingFiles from the Create→Edit hand-off; the ref guard prevents re-runs even if deps were included.
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

  const containerClass = [
    embedded
      ? 'space-y-3'
      : 'rounded-lg border border-border bg-card p-4 space-y-3',
    className ?? '',
  ]
    .filter(Boolean)
    .join(' ');

  const showList =
    attachments.length > 0 || pending.length > 0 || buffered.length > 0;

  return (
    <div className={containerClass}>
      <div className="space-y-0.5">
        <p className="text-sm font-medium">{title}</p>
        <p className="text-xs text-muted-foreground">{resolvedDescription}</p>
      </div>

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

      {showList && (
        <ul className="divide-y rounded-md border max-h-72 overflow-y-auto">
          {attachments.map((a) => {
            const uploadedAt = formatDate(a.uploadedAt);
            return (
              <li key={a.id} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
                <div className="min-w-0 flex-1">
                  <div className="truncate font-medium">{a.fileName}</div>
                  <div className="text-xs text-muted-foreground">
                    {formatSize(a.sizeBytes)}{uploadedAt ? ` · ${uploadedAt}` : ''}
                  </div>
                </div>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label={`Delete attachment ${a.fileName}`}
                  onClick={() => setConfirmDeleteId(a.id)}
                  className="text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                >
                  <X className="h-4 w-4" />
                </Button>
              </li>
            );
          })}
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
          {buffered.map((f, idx) => (
            <li
              key={`${f.name}-${idx}`}
              className="flex items-center justify-between gap-3 px-3 py-2 text-sm"
            >
              <div className="min-w-0 flex-1">
                <div className="truncate font-medium">{f.name}</div>
                <div className="text-xs text-muted-foreground">
                  {formatSize(f.size)} · Pending upload
                </div>
              </div>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                aria-label={`Remove pending file ${f.name}`}
                onClick={() => removeBufferedAt(idx)}
                className="text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
              >
                <X className="h-4 w-4" />
              </Button>
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
