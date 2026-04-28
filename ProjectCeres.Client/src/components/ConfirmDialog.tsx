import { useState } from 'react'
import { Check } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'

const ICONS: Record<string, React.ReactNode> = {
  check: <Check className="h-4 w-4" />,
}

interface ConfirmDialogProps {
  message: string
  confirmLabel?: string
  formId: string
  triggerLabel?: string
  triggerClassName?: string
  triggerIcon?: string
}

export function ConfirmDialog({
  message,
  confirmLabel = 'Confirm',
  formId,
  triggerLabel = 'Submit',
  triggerClassName,
  triggerIcon,
}: ConfirmDialogProps) {
  const [open, setOpen] = useState(false)

  function handleConfirm() {
    setOpen(false)
    const form = document.getElementById(formId) as HTMLFormElement | null
    form?.submit()
  }

  const icon = triggerIcon ? ICONS[triggerIcon] : null

  return (
    <>
      <button
        type="button"
        className={triggerClassName}
        onClick={() => setOpen(true)}
      >
        {icon}
        {triggerLabel}
      </button>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent showCloseButton={false}>
          <DialogHeader>
            <DialogTitle>Are you sure?</DialogTitle>
            <DialogDescription>{message}</DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button variant="destructive" onClick={handleConfirm}>
              {confirmLabel}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
