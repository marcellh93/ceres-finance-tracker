import { useId, useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { Field } from '../../components/Field';
import { SupportFilePicker } from './SupportFilePicker';
import { uploadSupportAttachment } from './support-api';

const MAX_BODY = 5000;

type ReplyComposerProps = {
  /**
   * Posts the reply and resolves the created message's id (or null on failure).
   * The composer then uploads any buffered files to that message.
   */
  onSubmit: (body: string) => Promise<string | null>;
  /** Fired after the reply AND its attachment uploads complete. */
  onComplete?: () => void;
  disabled?: boolean;
  label?: string;
  placeholder?: string;
  submitLabel?: string;
};

/**
 * The reply box under a thread. Controlled textarea + optional file buffer +
 * submit, matching the backend guard (StringLength 1..5000). On a successful
 * post it uploads any buffered files to the new message, then clears; a failure
 * leaves the draft and files in place so nothing typed is lost.
 */
export function ReplyComposer({
  onSubmit,
  onComplete,
  disabled = false,
  label = 'Your reply',
  placeholder = 'Add to the conversation…',
  submitLabel = 'Send reply',
}: ReplyComposerProps) {
  const id = useId();
  const [body, setBody] = useState('');
  const [files, setFiles] = useState<File[]>([]);
  const [pending, setPending] = useState(false);

  const trimmed = body.trim();
  const tooLong = body.length > MAX_BODY;
  const canSend = trimmed.length > 0 && !tooLong && !pending && !disabled;

  async function handleSubmit() {
    if (!canSend) return;
    setPending(true);
    try {
      const messageId = await onSubmit(trimmed);
      if (!messageId) return;
      await uploadBuffered(messageId, files);
      setBody('');
      setFiles([]);
      onComplete?.();
    } finally {
      setPending(false);
    }
  }

  return (
    <div className="space-y-2">
      <Field
        label={label}
        htmlFor={id}
        error={tooLong ? `Keep it under ${MAX_BODY.toLocaleString()} characters.` : undefined}
      >
        <Textarea
          id={id}
          value={body}
          onChange={(e) => setBody(e.target.value)}
          placeholder={placeholder}
          rows={4}
          disabled={pending || disabled}
          aria-invalid={tooLong || undefined}
        />
      </Field>
      <SupportFilePicker files={files} onChange={setFiles} disabled={pending || disabled} />
      <div className="flex justify-end">
        <Button size="sm" disabled={!canSend} onClick={() => void handleSubmit()}>
          {pending ? 'Sending…' : submitLabel}
        </Button>
      </div>
    </div>
  );
}

/** Upload each buffered file, surfacing per-file failures without losing the rest. */
async function uploadBuffered(messageId: string, files: File[]): Promise<void> {
  for (const file of files) {
    try {
      await uploadSupportAttachment(messageId, file);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : `Couldn't attach ${file.name}.`);
    }
  }
}
