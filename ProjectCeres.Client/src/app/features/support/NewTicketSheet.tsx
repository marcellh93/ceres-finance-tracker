import { useId, useState } from 'react';
import { Check, ChevronsUpDown } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { cn } from '@/lib/utils';
import { Field } from '../../components/Field';
import { apiFetch } from '../../lib/api-client';
import { SupportFilePicker } from './SupportFilePicker';
import {
  SUPPORT_TICKETS_URL,
  SupportTicketPriority,
  priorityLabel,
  uploadSupportAttachment,
  type CreateTicketRequest,
} from './support-api';

const MAX_SUBJECT = 200;
const MAX_MESSAGE = 5000;

const PRIORITY_ITEMS = [
  SupportTicketPriority.Low,
  SupportTicketPriority.Normal,
  SupportTicketPriority.High,
  SupportTicketPriority.Urgent,
].map((value) => ({ value, label: priorityLabel[value] }));

type NewTicketSheetProps = {
  onClose: () => void;
  /** Called with the new ticket id after a successful create. */
  onCreated: (ticketId: string) => void;
  /** When set, this is a follow-up continuing a Closed ticket. */
  followUp?: { precedingTicketId: string; subject: string } | null;
};

/**
 * The compose surface for a new ticket, in the same right-side sheet as a
 * thread. Doubles as the follow-up composer: when `followUp` is set the subject
 * is pre-seeded and the preceding ticket id rides along on create.
 */
export function NewTicketSheet({ onClose, onCreated, followUp }: NewTicketSheetProps) {
  const subjectId = useId();
  const messageId = useId();
  const [subject, setSubject] = useState(
    followUp ? `Re: ${followUp.subject}`.slice(0, MAX_SUBJECT) : '',
  );
  const [message, setMessage] = useState('');
  const [priority, setPriority] = useState<SupportTicketPriority>(SupportTicketPriority.Normal);
  const [files, setFiles] = useState<File[]>([]);
  const [pending, setPending] = useState(false);

  const subjectTrimmed = subject.trim();
  const messageTrimmed = message.trim();
  const subjectTooLong = subject.length > MAX_SUBJECT;
  const messageTooLong = message.length > MAX_MESSAGE;
  const canSubmit =
    subjectTrimmed.length > 0 &&
    messageTrimmed.length > 0 &&
    !subjectTooLong &&
    !messageTooLong &&
    !pending;

  async function handleSubmit() {
    if (!canSubmit) return;
    setPending(true);
    try {
      const body: CreateTicketRequest = {
        subject: subjectTrimmed,
        message: messageTrimmed,
        priority,
        precedingTicketId: followUp?.precedingTicketId ?? null,
      };
      const result = await apiFetch<{ id: string; firstMessageId: string }>(
        SUPPORT_TICKETS_URL,
        { method: 'POST', body },
      );
      if (!result.ok || !result.data) {
        toast.error(result.ok ? 'Could not open the ticket.' : result.message);
        return;
      }
      // Attachments hang off the first message, whose id the create response
      // now returns. Upload each buffered file; a per-file failure is surfaced
      // but does not undo the ticket.
      for (const file of files) {
        try {
          await uploadSupportAttachment(result.data.firstMessageId, file);
        } catch (err) {
          toast.error(err instanceof Error ? err.message : `Couldn't attach ${file.name}.`);
        }
      }
      toast.success('Ticket opened.');
      onCreated(result.data.id);
    } finally {
      setPending(false);
    }
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent
        className="data-[side=right]:w-full data-[side=right]:sm:max-w-lg"
        side="right"
      >
        <SheetHeader className="border-border border-b">
          <SheetTitle>{followUp ? 'Follow-up ticket' : 'New support ticket'}</SheetTitle>
          <SheetDescription>
            {followUp
              ? 'Continues a closed ticket. We keep the link so the history stays together.'
              : 'Tell us what you need. We reply by email and here in this thread.'}
          </SheetDescription>
        </SheetHeader>

        <div className="flex-1 space-y-4 overflow-y-auto overscroll-contain p-4">
          <Field
            label="Subject"
            htmlFor={subjectId}
            error={subjectTooLong ? `Keep it under ${MAX_SUBJECT} characters.` : undefined}
          >
            <Input
              id={subjectId}
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              placeholder="A short summary"
              disabled={pending}
              aria-invalid={subjectTooLong || undefined}
            />
          </Field>

          <Field label="Priority">
            <PriorityCombobox value={priority} onSelect={setPriority} disabled={pending} />
          </Field>

          <Field
            label="Message"
            htmlFor={messageId}
            error={
              messageTooLong ? `Keep it under ${MAX_MESSAGE.toLocaleString()} characters.` : undefined
            }
          >
            <Textarea
              id={messageId}
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              placeholder="What's going on?"
              rows={6}
              disabled={pending}
              aria-invalid={messageTooLong || undefined}
            />
          </Field>

          <Field label="Attachments">
            <SupportFilePicker files={files} onChange={setFiles} disabled={pending} />
          </Field>
        </div>

        <div className="border-border flex justify-end gap-2 border-t p-4">
          <Button variant="outline" size="sm" onClick={onClose} disabled={pending}>
            Cancel
          </Button>
          <Button size="sm" disabled={!canSubmit} onClick={() => void handleSubmit()}>
            {pending ? 'Opening…' : 'Open ticket'}
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}

function PriorityCombobox({
  value,
  onSelect,
  disabled,
}: {
  value: SupportTicketPriority;
  onSelect: (v: SupportTicketPriority) => void;
  disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            type="button"
            variant="outline"
            role="combobox"
            aria-expanded={open}
            className="w-full justify-between"
            disabled={disabled}
          >
            {priorityLabel[value]}
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="w-(--anchor-width) min-w-max p-0" align="start">
        <Command>
          <CommandList>
            <CommandEmpty>No options found.</CommandEmpty>
            <CommandGroup>
              {PRIORITY_ITEMS.map((item) => (
                <CommandItem
                  key={item.value}
                  value={item.label}
                  onSelect={() => {
                    onSelect(item.value);
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn(
                      'mr-2 h-4 w-4',
                      value === item.value ? 'opacity-100' : 'opacity-0',
                    )}
                  />
                  {item.label}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
