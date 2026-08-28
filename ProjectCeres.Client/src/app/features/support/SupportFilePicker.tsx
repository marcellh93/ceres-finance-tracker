import { useId, useRef } from 'react';
import { Paperclip, X } from 'lucide-react';
import { Button } from '@/components/ui/button';

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

type SupportFilePickerProps = {
  /** The buffered files, owned by the parent. */
  files: File[];
  onChange: (files: File[]) => void;
  disabled?: boolean;
};

/**
 * Buffers files client-side for a message that does not exist yet: on create
 * and on reply the message id is only known after the POST returns, so files
 * are held here and uploaded by the parent once it has that id. Mirrors the
 * movements AttachmentDropzone 'create' mode, trimmed for the composer.
 */
export function SupportFilePicker({ files, onChange, disabled = false }: SupportFilePickerProps) {
  const inputId = useId();
  const inputRef = useRef<HTMLInputElement | null>(null);

  function addFiles(list: FileList | null) {
    if (!list || list.length === 0) return;
    onChange([...files, ...Array.from(list)]);
  }

  function removeAt(index: number) {
    onChange(files.filter((_, i) => i !== index));
  }

  return (
    <div className="space-y-2">
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={disabled}
        onClick={() => inputRef.current?.click()}
      >
        <Paperclip aria-hidden="true" />
        Attach files
      </Button>
      <input
        ref={inputRef}
        id={inputId}
        type="file"
        multiple
        className="hidden"
        tabIndex={-1}
        aria-hidden="true"
        disabled={disabled}
        onChange={(e) => {
          addFiles(e.target.files);
          e.target.value = '';
        }}
      />

      {files.length > 0 && (
        <ul className="divide-border border-border divide-y rounded-md border">
          {files.map((file, index) => (
            <li
              key={`${file.name}-${index}`}
              className="flex items-center justify-between gap-3 px-3 py-2 text-sm"
            >
              <div className="min-w-0 flex-1">
                <div className="truncate font-medium">{file.name}</div>
                <div className="text-muted-foreground text-xs">{formatSize(file.size)}</div>
              </div>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                aria-label={`Remove ${file.name}`}
                disabled={disabled}
                onClick={() => removeAt(index)}
                className="text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
              >
                <X aria-hidden="true" />
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
